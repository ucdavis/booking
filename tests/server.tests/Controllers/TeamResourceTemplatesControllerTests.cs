using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.ResourceTemplates;
using Server.Services;

namespace Server.Tests.Controllers;

public class TeamResourceTemplatesControllerTests
{
    private const string FormJson = """
        {"fields":[{"id":"name","type":"input","label":"Your name"}]}
        """;
    private const string ChangedFormJson = """
        {"fields":[{"id":"purpose","type":"input","label":"Purpose"}]}
        """;

    [Fact]
    public async Task List_and_detail_expose_own_history_and_only_active_system_templates()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var active = Template("Team active", team.Id);
        var archived = Template("Team archived", team.Id, false);
        var system = Template("System active");
        var hidden = Template("System archived", isActive: false);
        var other = Template("Other team", team.Id + 1);
        db.ResourceTemplates.AddRange(active, archived, system, hidden, other);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = CreateController(db);

        var templates = ReadOk(await controller.GetTemplates(team.Slug));

        templates.Select(template => template.Name).Should().Equal("System active", "Team active", "Team archived");
        templates[0].TeamId.Should().BeNull();
        templates[1].TeamId.Should().Be(team.Id);
        ReadOk(await controller.GetTemplate(team.Slug, archived.Id)).IsActive.Should().BeFalse();
        ReadOk(await controller.GetTemplate(team.Slug, system.Id)).FormJson.Should().Be(FormJson);
        (await controller.GetTemplate(team.Slug, hidden.Id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.GetTemplate(team.Slug, other.Id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.GetTemplate(team.Slug, int.MaxValue)).Result.Should().BeOfType<NotFoundObjectResult>();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(TeamRole.Editor, false)]
    [InlineData(TeamRole.Admin, false)]
    [InlineData(null, true)]
    public async Task Editors_and_above_create_a_team_owned_template_with_the_effective_user_audit(TeamRole? role, bool siteAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db, role, siteAdmin);

        var result = await CreateController(db).CreateTemplate(team.Slug, Request("  Team template  "));

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<ResourceTemplateResponse>().Subject;
        created.ActionName.Should().Be(nameof(TeamResourceTemplatesController.GetTemplate));
        created.RouteValues!["teamSlug"].Should().Be(team.Slug);
        created.RouteValues["id"].Should().Be(response.Id);
        response.Name.Should().Be("Team template");
        response.TeamId.Should().Be(team.Id);
        response.FormSchemaVersion.Should().Be(1);
        response.IsActive.Should().BeTrue();
        db.ChangeTracker.Clear();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.TeamId.Should().Be(team.Id);
        saved.UpdatedByUserId.Should().Be(user.Id);
    }

    [Theory]
    [InlineData(TeamRole.Viewer, true, "member-iam", true)]
    [InlineData(null, true, "member-iam", true)]
    [InlineData((TeamRole)999, true, "member-iam", true)]
    [InlineData(TeamRole.Editor, false, "member-iam", true)]
    [InlineData(TeamRole.Editor, true, "missing", true)]
    [InlineData(TeamRole.Editor, true, null, true)]
    [InlineData(TeamRole.Editor, true, "member-iam", false)]
    public async Task Every_action_rechecks_active_editor_access_and_never_uses_the_emulation_actor(
        TeamRole? role, bool active, string? iamId, bool authenticated)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db, role, isActive: active);
        var template = Template("Original", team.Id);
        db.ResourceTemplates.Add(template);
        db.Users.Add(new User { IamId = "actor-iam", Name = "Actor", IsAdmin = true });
        await db.SaveChangesAsync();
        var controller = CreateController(db, iamId, authenticated);
        controller.HttpContext.Items[EmulationService.ActorItemKey] = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("ucdPersonIAMID", "actor-iam")], "Test"));

        (await controller.GetTemplates(team.Slug)).Result.Should().BeOfType<ForbidResult>();
        (await controller.GetTemplate(team.Slug, template.Id)).Result.Should().BeOfType<ForbidResult>();
        (await controller.CreateTemplate(team.Slug, Request("New"))).Result.Should().BeOfType<ForbidResult>();
        (await controller.UpdateTemplate(team.Slug, template.Id, Request("Changed", template))).Result.Should().BeOfType<ForbidResult>();
        (await controller.DuplicateTemplate(team.Slug, template.Id)).Result.Should().BeOfType<ForbidResult>();
        (await db.ResourceTemplates.SingleAsync()).Name.Should().Be("Original");
    }

    [Fact]
    public async Task Access_is_scoped_to_the_route_team_and_missing_teams_return_not_found_for_site_admins()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (_, user) = await SeedTeam(db);
        db.Teams.Add(new Team { Name = "Other", Slug = "other" });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        (await controller.GetTemplates("other")).Result.Should().BeOfType<ForbidResult>();
        (await controller.CreateTemplate("other", Request("Cross-team"))).Result.Should().BeOfType<ForbidResult>();
        user.IsAdmin = true;
        await db.SaveChangesAsync();
        (await controller.GetTemplates("missing")).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.CreateTemplate("missing", Request("Missing"))).Result.Should().BeOfType<NotFoundObjectResult>();
        (await db.ResourceTemplates.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplication_copies_active_system_or_archived_own_template_into_the_team(bool archivedTeamSource)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var source = Template("Source", archivedTeamSource ? team.Id : null, !archivedTeamSource);
        source.Description = "Reusable description";
        source.FormSchemaVersion = 4;
        source.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.DuplicateTemplate(team.Slug, source.Id);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var copy = created.Value.Should().BeOfType<ResourceTemplateResponse>().Subject;
        created.RouteValues!["teamSlug"].Should().Be(team.Slug);
        copy.Id.Should().NotBe(source.Id);
        copy.TeamId.Should().Be(team.Id);
        copy.Name.Should().Be("Source (copy)");
        copy.Description.Should().Be(source.Description);
        copy.FormSchemaVersion.Should().Be(1);
        copy.FormJson.Should().Be(FormJson);
        copy.ResourceDefaultsJson.Should().Be(source.ResourceDefaultsJson);
        copy.IsActive.Should().BeTrue();
        db.ChangeTracker.Clear();
        var savedCopy = await db.ResourceTemplates.SingleAsync(template => template.Id == copy.Id);
        savedCopy.UpdatedByUserId.Should().Be(user.Id);
        var savedSource = await db.ResourceTemplates.SingleAsync(template => template.Id == source.Id);
        savedSource.TeamId.Should().Be(source.TeamId);
        savedSource.FormSchemaVersion.Should().Be(4);
        savedSource.IsActive.Should().Be(!archivedTeamSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hidden_system_and_other_team_templates_cannot_be_duplicated_even_by_site_admins(bool siteAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db, isAdmin: siteAdmin);
        var hidden = Template("Inactive system", isActive: false);
        var other = Template("Other team", team.Id + 1);
        db.ResourceTemplates.AddRange(hidden, other);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        (await controller.DuplicateTemplate(team.Slug, hidden.Id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.DuplicateTemplate(team.Slug, other.Id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await db.ResourceTemplates.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task System_and_other_team_templates_cannot_be_edited_from_a_team_route_even_by_site_admins()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db, isAdmin: true);
        var system = Template("System");
        var other = Template("Other team", team.Id + 1);
        db.ResourceTemplates.AddRange(system, other);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        (await controller.UpdateTemplate(team.Slug, system.Id, Request("Changed", system))).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.UpdateTemplate(team.Slug, other.Id, Request("Changed", other))).Result.Should().BeOfType<NotFoundObjectResult>();
        db.ChangeTracker.Clear();
        (await db.ResourceTemplates.OrderBy(template => template.Id).Select(template => template.Name).ToListAsync())
            .Should().Equal("System", "Other team");
    }

    [Fact]
    public async Task Form_edits_keep_both_team_versions_and_archived_versions_cannot_be_changed()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var source = Template("Original", team.Id);
        source.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = CreateController(db);
        var request = Request("Revised", source, ChangedFormJson);

        var result = await controller.UpdateTemplate(team.Slug, source.Id, request);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var revision = created.Value.Should().BeOfType<ResourceTemplateResponse>().Subject;
        created.RouteValues!["teamSlug"].Should().Be(team.Slug);
        revision.TeamId.Should().Be(team.Id);
        revision.Id.Should().NotBe(source.Id);
        revision.FormSchemaVersion.Should().Be(2);
        revision.FormJson.Should().Be(ChangedFormJson);
        revision.ResourceDefaultsJson.Should().Be(source.ResourceDefaultsJson);
        revision.IsActive.Should().BeTrue();
        var archived = ReadOk(await controller.GetTemplate(team.Slug, source.Id));
        archived.TeamId.Should().Be(team.Id);
        archived.Name.Should().Be("Original");
        archived.FormJson.Should().Be(FormJson);
        archived.IsActive.Should().BeFalse();
        ReadOk(await controller.GetTemplates(team.Slug)).Should().HaveCount(2);
        (await controller.UpdateTemplate(team.Slug, source.Id, request)).Result.Should().BeOfType<ConflictObjectResult>();
        db.ChangeTracker.Clear();
        var versions = await db.ResourceTemplates.ToListAsync();
        versions.Should().HaveCount(2);
        versions.Should().OnlyContain(template => template.TeamId == team.Id && template.UpdatedByUserId == user.Id);
    }

    [Fact]
    public async Task Team_updates_reject_stale_timestamps_without_mutating_the_template()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var source = Template("Original", team.Id);
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var request = Request("First edit", source);
        var controller = CreateController(db);

        ReadOk(await controller.UpdateTemplate(team.Slug, source.Id, request)).Name.Should().Be("First edit");
        (await controller.UpdateTemplate(team.Slug, source.Id, request)).Result.Should().BeOfType<ConflictObjectResult>();
        (await db.ResourceTemplates.SingleAsync()).Name.Should().Be("First edit");
    }

    [Fact]
    public void Controller_requires_editor_policy_antiforgery_and_no_caching_and_shared_methods_are_not_actions()
    {
        var controller = typeof(TeamResourceTemplatesController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/teams/{teamSlug}/resource-templates");
        controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(AuthenticationHelper.TeamEditorPolicy);
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        typeof(ResourceTemplatesControllerBase).IsAbstract.Should().BeTrue();
        typeof(ResourceTemplatesControllerBase).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();
    }

    private static T ReadOk<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static SaveResourceTemplateRequest Request(string name, ResourceTemplate? saved = null, string formJson = FormJson) => new()
    {
        Name = name, FormSchemaVersion = saved?.FormSchemaVersion ?? 1, FormJson = formJson,
        IsActive = true, UpdatedAt = saved?.UpdatedAt,
    };

    private static ResourceTemplate Template(string name, int? teamId = null, bool isActive = true) => new()
    {
        Name = name, TeamId = teamId, FormSchemaVersion = 1, FormJson = FormJson, IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1), UpdatedByUserId = 99,
    };

    private static async Task<(Team Team, User User)> SeedTeam(
        AppDbContext db, TeamRole? role = TeamRole.Editor, bool isAdmin = false, bool isActive = true)
    {
        var user = new User { IamId = "member-iam", Name = "Member", IsAdmin = isAdmin, IsActive = isActive };
        var team = new Team { Name = "Team A", Slug = "team-a" };
        db.Users.Add(user);
        db.Teams.Add(team);
        if (role != null)
        {
            db.TeamPermissions.Add(new TeamPermission { Team = team, User = user, Role = role.Value });
        }
        await db.SaveChangesAsync();
        return (team, user);
    }

    private static TeamResourceTemplatesController CreateController(
        AppDbContext db, string? iamId = "member-iam", bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new TeamResourceTemplatesController(db, new TeamAccessService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null)),
                },
            },
        };
    }
}

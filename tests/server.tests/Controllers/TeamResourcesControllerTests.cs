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
using Server.Models.Resources;
using Server.Services;

namespace Server.Tests.Controllers;

public class TeamResourcesControllerTests
{
    private const string FormJson = """
        {"fields":[{"id":"name","type":"input","label":"Your name"}]}
        """;
    private const string ChangedFormJson = """
        {"fields":[{"id":"purpose","type":"textarea","label":"Purpose"}]}
        """;
    private const string DetailsJson = """
        {"schemaVersion":1,"futureSetting":{"mode":"preserve"}}
        """;
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_and_detail_include_inactive_resources_and_exclude_other_teams()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (active, _) = await SeedResource(db, team, space, user, "Active room");
        var (inactive, _) = await SeedResource(db, team, space, user, "Inactive room", isActive: false);
        var otherTeam = new Team { Name = "Other", Slug = "other" };
        db.Teams.Add(otherTeam);
        await db.SaveChangesAsync();
        var (other, _) = await SeedResource(db, otherTeam, space, user, "Other room");
        db.ChangeTracker.Clear();
        var controller = CreateController(db);

        var resources = ReadOk(await controller.GetResources(team.Slug));

        resources.Select(resource => resource.Id).Should().BeEquivalentTo(new[] { active.Id, inactive.Id });
        ReadOk(await controller.GetResource(team.Slug, inactive.Id)).IsActive.Should().BeFalse();
        (await controller.GetResource(team.Slug, other.Id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.GetResource(team.Slug, int.MaxValue)).Result.Should().BeOfType<NotFoundObjectResult>();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Options_only_include_active_linked_spaces_and_active_own_or_system_templates()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var linked = await SeedSpace(db, team);
        await SeedSpace(db, team, "Inactive location", isActive: false);
        await SeedSpace(db, null, "Unlinked location");
        var own = Template("Own", team.Id);
        var system = Template("System");
        db.ResourceTemplates.AddRange(own, system, Template("Archived own", team.Id, false),
            Template("Archived system", isActive: false), Template("Other", team.Id + 1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var options = ReadOk(await CreateController(db).GetOptions(team.Slug));

        options.Spaces.Select(space => space.Id).Should().Equal(linked.Id);
        options.Templates.Select(template => template.Id).Should().BeEquivalentTo(new[] { own.Id, system.Id });
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(TeamRole.Editor, false)]
    [InlineData(TeamRole.Admin, false)]
    [InlineData(null, true)]
    public async Task Editors_and_above_create_a_root_resource_and_published_configuration_with_effective_user_audit(
        TeamRole? role, bool siteAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db, role, isAdmin: siteAdmin);
        var space = await SeedSpace(db, team);
        var template = Template("Template", team.Id);
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();

        var result = await CreateController(db).CreateResource(team.Slug,
            Request(space.Id, template.Id, name: "  Meeting room  "));

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<ResourceDetailResponse>().Subject;
        created.ActionName.Should().Be(nameof(TeamResourcesController.GetResource));
        created.RouteValues!["teamSlug"].Should().Be(team.Slug);
        created.RouteValues["id"].Should().Be(response.Id);
        response.Name.Should().Be("Meeting room");
        db.ChangeTracker.Clear();
        var resource = await db.Resources.SingleAsync();
        resource.TeamId.Should().Be(team.Id);
        resource.SpaceId.Should().Be(space.Id);
        resource.ParentResourceId.Should().BeNull();
        resource.NameKey.Should().Be("MEETING ROOM");
        resource.Slug.Should().NotBeNullOrWhiteSpace();
        resource.IsActive.Should().BeTrue();
        resource.UpdatedByUserId.Should().Be(user.Id);
        var config = await db.ResourceConfigs.SingleAsync();
        config.ResourceId.Should().Be(resource.Id);
        config.Version.Should().Be(1);
        config.SourceTemplateId.Should().Be(template.Id);
        config.CreatedByUserId.Should().Be(user.Id);
        config.PublishedByUserId.Should().Be(user.Id);
        config.PublishedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(TeamRole.Viewer, true, "member-iam", true)]
    [InlineData(TeamRole.Approver, true, "member-iam", true)]
    [InlineData(null, true, "member-iam", true)]
    [InlineData((TeamRole)999, true, "member-iam", true)]
    [InlineData(TeamRole.Editor, false, "member-iam", true)]
    [InlineData(TeamRole.Editor, true, "missing", true)]
    [InlineData(TeamRole.Editor, true, null, true)]
    [InlineData(TeamRole.Editor, true, "member-iam", false)]
    public async Task Every_action_rechecks_active_editor_access_and_does_not_use_the_emulation_actor(
        TeamRole? role, bool active, string? iamId, bool authenticated)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db, role, isActive: active);
        var space = await SeedSpace(db, team);
        var (resource, config) = await SeedResource(db, team, space, user);
        db.Users.Add(new User { IamId = "actor-iam", Name = "Actor", IsAdmin = true });
        await db.SaveChangesAsync();
        var controller = CreateController(db, iamId, authenticated);
        controller.HttpContext.Items[EmulationService.ActorItemKey] = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("ucdPersonIAMID", "actor-iam")], "Test"));

        (await controller.GetResources(team.Slug)).Result.Should().BeOfType<ForbidResult>();
        (await controller.GetOptions(team.Slug)).Result.Should().BeOfType<ForbidResult>();
        (await controller.GetResource(team.Slug, resource.Id)).Result.Should().BeOfType<ForbidResult>();
        (await controller.CreateResource(team.Slug, Request(space.Id))).Result.Should().BeOfType<ForbidResult>();
        (await controller.UpdateResource(team.Slug, resource.Id, Request(saved: resource, config: config, name: "Changed")))
            .Result.Should().BeOfType<ForbidResult>();
        (await db.Resources.SingleAsync()).Name.Should().Be("Meeting room");
        (await db.ResourceConfigs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Team_membership_does_not_grant_other_team_access_and_site_admins_cannot_edit_cross_team_ids()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var otherTeam = new Team { Name = "Other", Slug = "other" };
        db.Teams.Add(otherTeam);
        await db.SaveChangesAsync();
        var space = await SeedSpace(db, otherTeam);
        var (resource, config) = await SeedResource(db, otherTeam, space, user);
        var controller = CreateController(db);

        (await controller.GetResources(otherTeam.Slug)).Result.Should().BeOfType<ForbidResult>();
        (await controller.CreateResource(otherTeam.Slug, Request(space.Id))).Result.Should().BeOfType<ForbidResult>();
        user.IsAdmin = true;
        await db.SaveChangesAsync();
        (await controller.GetResource(team.Slug, resource.Id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.UpdateResource(team.Slug, resource.Id, Request(saved: resource, config: config, name: "Changed")))
            .Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.GetResources("missing")).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.GetOptions("missing")).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.CreateResource("missing", Request(space.Id))).Result.Should().BeOfType<NotFoundObjectResult>();
        (await db.Resources.SingleAsync()).Name.Should().Be("Meeting room");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creation_copies_template_documents_once_including_unknown_defaults(bool systemTemplate)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var template = Template("Original", systemTemplate ? null : team.Id);
        template.FormSchemaVersion = 4;
        template.ResourceDefaultsJson = DetailsJson;
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();

        var created = ReadCreated(await CreateController(db).CreateResource(team.Slug, Request(space.Id, template.Id)));
        db.ChangeTracker.Clear();
        template = await db.ResourceTemplates.SingleAsync();
        template.FormJson = ChangedFormJson;
        template.ResourceDefaultsJson = "{}";
        template.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var config = await db.ResourceConfigs.SingleAsync();
        config.ResourceId.Should().Be(created.Id);
        config.SourceTemplateId.Should().Be(template.Id);
        config.FormSchemaVersion.Should().Be(4);
        config.FormJson.Should().Be(FormJson);
        config.DetailsJson.Should().Be(DetailsJson);
        config.DetailsSchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task Creation_preserves_future_template_formats_exactly_when_configuration_edits_are_omitted()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        const string futureForm = "{\"fields\":[],\"futureFormSetting\":true}";
        const string futureDefaults = "{\"schemaVersion\":99,\"futureAmount\":9007199254740993}";
        var template = Template("Future template", team.Id);
        template.FormSchemaVersion = 7;
        template.FormJson = futureForm;
        template.ResourceDefaultsJson = futureDefaults;
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();

        ReadCreated(await CreateController(db).CreateResource(team.Slug, Request(space.Id, template.Id)));

        db.ChangeTracker.Clear();
        var config = await db.ResourceConfigs.SingleAsync();
        config.FormSchemaVersion.Should().Be(7);
        config.FormJson.Should().Be(futureForm);
        config.DetailsSchemaVersion.Should().Be(99);
        config.DetailsJson.Should().Be(futureDefaults);
    }

    [Fact]
    public async Task Template_without_defaults_starts_with_an_empty_details_document()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var template = Template("Template", team.Id);
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();

        ReadCreated(await CreateController(db).CreateResource(team.Slug, Request(space.Id, template.Id)));

        (await db.ResourceConfigs.SingleAsync()).DetailsJson.Should().Be("{}");
    }

    [Fact]
    public async Task Creation_can_save_and_link_a_new_nonofficial_location_with_the_resource()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var template = Template("Template");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var location = new NewResourceSpaceRequest
        {
            Name = "  Orchard building  ", Address = "  123 Orchard Road  ",
        };

        ReadCreated(await CreateController(db).CreateResource(team.Slug, Request(templateId: template.Id, newSpace: location)));

        db.ChangeTracker.Clear();
        var space = await db.Spaces.SingleAsync();
        space.Name.Should().Be("Orchard building");
        space.Address.Should().Be("123 Orchard Road");
        space.TimeZoneId.Should().Be("America/Los_Angeles");
        space.IsOfficialFacility.Should().BeFalse();
        space.IsActive.Should().BeTrue();
        space.Slug.Should().NotBeNullOrWhiteSpace();
        var link = await db.TeamSpaces.SingleAsync();
        link.TeamId.Should().Be(team.Id);
        link.SpaceId.Should().Be(space.Id);
        (await db.Resources.SingleAsync()).SpaceId.Should().Be(space.Id);
        (await db.ResourceConfigs.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("location-name")]
    [InlineData("form")]
    [InlineData("details")]
    public async Task Invalid_creation_leaves_no_location_link_resource_or_configuration(string invalidPart)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db);
        var template = Template("Template");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var request = Request(templateId: template.Id,
            formJson: invalidPart == "form" ? "{\"fields\":[{\"id\":\"unsupported\",\"type\":\"future\",\"label\":\"Future\"}]}" : null,
            detailsJson: invalidPart == "details" ? "[]" : null,
            newSpace: new NewResourceSpaceRequest
            {
                Name = invalidPart == "location-name" ? "  " : "New location",
            });

        (await CreateController(db).CreateResource(team.Slug, request)).Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.Clear();
        (await db.Spaces.AnyAsync()).Should().BeFalse();
        (await db.TeamSpaces.AnyAsync()).Should().BeFalse();
        (await db.Resources.AnyAsync()).Should().BeFalse();
        (await db.ResourceConfigs.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("inactive-own-template")]
    [InlineData("inactive-system-template")]
    [InlineData("other-team-template")]
    [InlineData("missing-template")]
    [InlineData("no-template")]
    [InlineData("inactive-space")]
    [InlineData("unlinked-space")]
    [InlineData("missing-space")]
    [InlineData("no-space")]
    public async Task Creation_rejects_unavailable_or_out_of_scope_template_and_location_selections(string selection)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, _) = await SeedTeam(db, isAdmin: true);
        var space = await SeedSpace(db, selection == "unlinked-space" ? null : team,
            isActive: selection != "inactive-space");
        var template = Template("Template", selection == "other-team-template" ? team.Id + 1 :
            selection == "inactive-system-template" ? null : team.Id,
            isActive: selection != "inactive-own-template" && selection != "inactive-system-template");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        int? spaceId = selection == "no-space" ? null : selection == "missing-space" ? int.MaxValue : space.Id;
        int? templateId = selection == "no-template" ? null : selection == "missing-template" ? int.MaxValue : template.Id;

        (await CreateController(db).CreateResource(team.Slug, Request(spaceId, templateId)))
            .Result.Should().BeOfType<BadRequestObjectResult>();

        (await db.Resources.AnyAsync()).Should().BeFalse();
        (await db.ResourceConfigs.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_sibling_names_are_rejected_across_teams_sharing_a_location(bool update)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var otherTeam = new Team { Name = "Other", Slug = "other" };
        var template = Template("Template");
        db.Teams.Add(otherTeam);
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        await SeedResource(db, otherTeam, space, user);
        var controller = CreateController(db);

        if (update)
        {
            var (resource, config) = await SeedResource(db, team, space, user, "Distinct room");
            (await controller.UpdateResource(team.Slug, resource.Id,
                Request(saved: resource, config: config, name: "  MEETING ROOM  "))).Result.Should().BeOfType<ConflictObjectResult>();
            db.ChangeTracker.Clear();
            (await db.Resources.SingleAsync(value => value.Id == resource.Id)).Name.Should().Be("Distinct room");
        }
        else
        {
            (await controller.CreateResource(team.Slug, Request(space.Id, template.Id, name: "  MEETING ROOM  ")))
                .Result.Should().BeOfType<ConflictObjectResult>();
            (await db.Resources.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task Updates_reject_stale_resource_timestamps_and_configuration_ids_without_changing_saved_data()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (resource, config) = await SeedResource(db, team, space, user);
        var request = Request(saved: resource, config: config, name: "First edit");
        var controller = CreateController(db);

        ReadOk(await controller.UpdateResource(team.Slug, resource.Id, request)).Name.Should().Be("First edit");
        (await controller.UpdateResource(team.Slug, resource.Id, request)).Result.Should().BeOfType<ConflictObjectResult>();
        db.ChangeTracker.Clear();
        var saved = await db.Resources.SingleAsync();
        var wrongConfig = new ResourceConfig { Id = config.Id + 1, FormJson = FormJson, DetailsJson = DetailsJson };
        (await controller.UpdateResource(team.Slug, resource.Id,
            Request(saved: saved, config: wrongConfig, name: "Stale config edit"))).Result.Should().BeOfType<ConflictObjectResult>();
        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync()).Name.Should().Be("First edit");
        (await db.ResourceConfigs.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("timestamp", 400)]
    [InlineData("configuration", 409)]
    public async Task Updates_require_both_concurrency_values(string missingValue, int expectedStatus)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (resource, config) = await SeedResource(db, team, space, user);
        var request = new SaveResourceRequest
        {
            Name = "Changed", Category = "room",
            UpdatedAt = missingValue == "timestamp" ? null : resource.UpdatedAt,
            ExpectedConfigId = missingValue == "configuration" ? null : config.Id,
        };

        var result = await CreateController(db).UpdateResource(team.Slug, resource.Id, request);

        result.Result.Should().BeAssignableTo<ObjectResult>().Subject.StatusCode.Should().Be(expectedStatus);
        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync()).Name.Should().Be("Meeting room");
        (await db.ResourceConfigs.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"future\"}")]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{\"schemaVersion\":{}}")]
    [InlineData("{\"openingHours\":{\"monday\":[{\"start\":\"09:00\",\"end\":\"12:00\"},{\"start\":\"11:00\",\"end\":\"13:00\"}]}}")]
    [InlineData("{\"billingRates\":[{\"id\":\"rate\",\"name\":\"Hourly\",\"amount\":\"-1\",\"basis\":\"hour\"}]}")]
    [InlineData("{\"billingRates\":[{\"id\":\"rate\",\"name\":\"Hourly\",\"amount\":\"10\",\"basis\":\"hour\",\"currency\":\"EUR\"}]}")]
    public async Task Invalid_settings_edits_return_bad_request_without_overwriting_published_settings(string details)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (resource, config) = await SeedResource(db, team, space, user);

        (await CreateController(db).UpdateResource(team.Slug, resource.Id,
            Request(saved: resource, config: config, name: "Changed", detailsJson: details)))
            .Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync()).Name.Should().Be("Meeting room");
        (await db.ResourceConfigs.SingleAsync()).DetailsJson.Should().Be(DetailsJson);
    }

    [Fact]
    public async Task Configuration_edits_publish_a_new_version_and_preserve_the_version_used_by_existing_reservations()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var template = Template("Template", team.Id);
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var (resource, original) = await SeedResource(db, team, space, user, template: template);
        var reservation = await SeedReservation(db, resource, original, user);
        var controller = CreateController(db);
        const string changedDetails = "{\"schemaVersion\":1,\"futureSetting\":{\"mode\":\"preserve\"},\"openingHours\":{\"monday\":[{\"start\":\"09:00\",\"end\":\"17:00\"}]}}";

        ReadOk(await controller.UpdateResource(team.Slug, resource.Id,
            Request(saved: resource, config: original, formJson: ChangedFormJson, detailsJson: changedDetails)));

        db.ChangeTracker.Clear();
        var configs = await db.ResourceConfigs.OrderBy(config => config.Version).ToListAsync();
        configs.Should().HaveCount(2);
        configs[0].Id.Should().Be(original.Id);
        configs[0].FormJson.Should().Be(FormJson);
        configs[0].DetailsJson.Should().Be(DetailsJson);
        configs[0].UpdatedAt.Should().Be(SavedAt);
        configs[0].PublishedAt.Should().Be(SavedAt);
        configs[1].Version.Should().Be(2);
        configs[1].SourceTemplateId.Should().Be(template.Id);
        configs[1].FormJson.Should().Be(ChangedFormJson);
        configs[1].DetailsJson.Should().Be(changedDetails);
        configs[1].CreatedByUserId.Should().Be(user.Id);
        configs[1].PublishedByUserId.Should().Be(user.Id);
        configs[1].PublishedAt.Should().NotBeNull();
        (await db.ReservationSeries.SingleAsync(value => value.Id == reservation.Id)).ResourceConfigId.Should().Be(original.Id);
    }

    [Fact]
    public async Task Metadata_edits_preserve_omitted_unsupported_configuration_documents_without_a_new_version()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (resource, config) = await SeedResource(db, team, space, user);
        const string futureForm = "{\"fields\":[],\"futureFormSetting\":true}";
        const string futureDetails = "{\"schemaVersion\":99,\"futureDetailsSetting\":{\"keep\":true}}";
        config.FormJson = futureForm;
        config.DetailsSchemaVersion = 99;
        config.DetailsJson = futureDetails;
        await db.SaveChangesAsync();

        ReadOk(await CreateController(db).UpdateResource(team.Slug, resource.Id,
            Request(saved: resource, config: config, name: "Renamed room")));

        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync()).Name.Should().Be("Renamed room");
        var saved = await db.ResourceConfigs.SingleAsync();
        saved.Id.Should().Be(config.Id);
        saved.FormJson.Should().Be(futureForm);
        saved.DetailsJson.Should().Be(futureDetails);
        saved.DetailsSchemaVersion.Should().Be(99);
        saved.UpdatedAt.Should().Be(SavedAt);
    }

    [Fact]
    public async Task Renaming_a_child_preserves_its_slug_hierarchy_and_inherited_public_access()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (parent, _) = await SeedResource(db, team, space, user, "Parent room");
        var (child, config) = await SeedResource(db, team, space, user, "Child room", parent: parent);
        var originalSlug = child.Slug;
        var request = new SaveResourceRequest
        {
            Name = "Renamed child", Category = child.Category, Capacity = child.Capacity,
            PublicAccess = "details", UpdatedAt = child.UpdatedAt, ExpectedConfigId = config.Id,
        };

        ReadOk(await CreateController(db).UpdateResource(team.Slug, child.Id, request)).PublicAccess.Should().BeNull();

        db.ChangeTracker.Clear();
        var saved = await db.Resources.SingleAsync(resource => resource.Id == child.Id);
        saved.Name.Should().Be("Renamed child");
        saved.Slug.Should().Be(originalSlug);
        saved.ParentResourceId.Should().Be(parent.Id);
        saved.TeamId.Should().Be(team.Id);
        saved.SpaceId.Should().Be(space.Id);
        saved.PublicAccess.Should().BeNull();
        (await db.ResourceConfigs.CountAsync(resourceConfig => resourceConfig.ResourceId == child.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Deactivation_and_reactivation_preserve_configuration_and_reservations()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (resource, config) = await SeedResource(db, team, space, user);
        var reservation = await SeedReservation(db, resource, config, user);
        var controller = CreateController(db);

        ReadOk(await controller.UpdateResource(team.Slug, resource.Id,
            Request(saved: resource, config: config, isActive: false))).IsActive.Should().BeFalse();
        db.ChangeTracker.Clear();
        var inactive = await db.Resources.SingleAsync();
        inactive.IsActive.Should().BeFalse();
        ReadOk(await controller.UpdateResource(team.Slug, resource.Id,
            Request(saved: inactive, config: config))).IsActive.Should().BeTrue();

        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync()).IsActive.Should().BeTrue();
        (await db.ResourceConfigs.SingleAsync()).Id.Should().Be(config.Id);
        var savedReservation = await db.ReservationSeries.SingleAsync();
        savedReservation.Id.Should().Be(reservation.Id);
        savedReservation.ResourceConfigId.Should().Be(config.Id);
        savedReservation.FormResponsesJson.Should().Be("{\"name\":\"Requester\"}");
    }

    [Fact]
    public async Task A_resource_with_active_children_cannot_be_deactivated()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var (parent, config) = await SeedResource(db, team, space, user);
        await SeedResource(db, team, space, user, "Child room", parent: parent);

        (await CreateController(db).UpdateResource(team.Slug, parent.Id,
            Request(saved: parent, config: config, isActive: false))).Result.Should().BeOfType<ConflictObjectResult>();

        db.ChangeTracker.Clear();
        (await db.Resources.ToListAsync()).Should().OnlyContain(resource => resource.IsActive);
        (await db.ResourceConfigs.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activation_requires_an_active_location_and_active_ancestors(bool inactiveAncestor)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team, isActive: inactiveAncestor);
        Resource? ancestor = null;
        if (inactiveAncestor)
        {
            (ancestor, _) = await SeedResource(db, team, space, user, "Ancestor", isActive: false);
        }
        var (resource, config) = await SeedResource(db, team, space, user, isActive: false, parent: ancestor);

        (await CreateController(db).UpdateResource(team.Slug, resource.Id,
            Request(saved: resource, config: config))).Result.Should().BeOfType<ConflictObjectResult>();

        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync(value => value.Id == resource.Id)).IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("location")]
    [InlineData("new-location")]
    [InlineData("template")]
    public async Task Update_cannot_change_resource_location_or_source_template(string change)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var (team, user) = await SeedTeam(db);
        var space = await SeedSpace(db, team);
        var anotherSpace = await SeedSpace(db, team, "Other location");
        var originalTemplate = Template("Original", team.Id);
        var anotherTemplate = Template("Another", team.Id);
        db.ResourceTemplates.AddRange(originalTemplate, anotherTemplate);
        await db.SaveChangesAsync();
        var (resource, config) = await SeedResource(db, team, space, user, template: originalTemplate);
        var request = Request(saved: resource, config: config,
            spaceId: change == "location" ? anotherSpace.Id : null,
            templateId: change == "template" ? anotherTemplate.Id : null,
            newSpace: change == "new-location" ? new NewResourceSpaceRequest
            {
                Name = "New location",
            } : null);

        (await CreateController(db).UpdateResource(team.Slug, resource.Id, request)).Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.Clear();
        (await db.Resources.SingleAsync()).SpaceId.Should().Be(space.Id);
        (await db.ResourceConfigs.SingleAsync()).SourceTemplateId.Should().Be(originalTemplate.Id);
        (await db.Spaces.CountAsync()).Should().Be(2);
    }

    [Fact]
    public void Controller_requires_editor_policy_antiforgery_and_no_caching()
    {
        var controller = typeof(TeamResourcesController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/teams/{teamSlug}/resources");
        controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(AuthenticationHelper.TeamEditorPolicy);
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
    }

    private static T ReadOk<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static ResourceDetailResponse ReadCreated(ActionResult<ResourceDetailResponse> result)
        => result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value.Should().BeOfType<ResourceDetailResponse>().Subject;

    private static SaveResourceRequest Request(int? spaceId = null, int? templateId = null,
        Resource? saved = null, ResourceConfig? config = null, string name = "Meeting room", bool isActive = true,
        string? formJson = null, string? detailsJson = null, NewResourceSpaceRequest? newSpace = null) => new()
    {
        Name = name, Category = saved?.Category ?? "room", Description = saved?.Description,
        Capacity = saved?.Capacity, IsActive = isActive, IsReservable = saved?.IsReservable ?? true,
        ApprovalMode = saved?.ApprovalMode ?? "manual", FollowTeamCalendar = saved?.FollowTeamCalendar ?? true,
        PublicAccess = saved == null ? "availability" : saved.PublicAccess,
        AllowReservationSharing = saved?.AllowReservationSharing ?? false,
        SpaceId = spaceId, NewSpace = newSpace, SourceTemplateId = templateId,
        FormSchemaVersion = formJson == null ? null : config?.FormSchemaVersion ?? 1, FormJson = formJson,
        DetailsSchemaVersion = detailsJson == null ? null : config?.DetailsSchemaVersion ?? 1, DetailsJson = detailsJson,
        UpdatedAt = saved?.UpdatedAt, ExpectedConfigId = config?.Id,
    };

    private static ResourceTemplate Template(string name, int? teamId = null, bool isActive = true) => new()
    {
        Name = name, TeamId = teamId, FormSchemaVersion = 1, FormJson = FormJson, IsActive = isActive,
        CreatedAt = SavedAt, UpdatedAt = SavedAt, UpdatedByUserId = 99,
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

    private static async Task<Space> SeedSpace(AppDbContext db, Team? team,
        string name = "Main location", bool isActive = true)
    {
        var space = new Space
        {
            Name = name, Slug = name.ToLowerInvariant().Replace(' ', '-'), TimeZoneId = "America/Los_Angeles",
            IsActive = isActive, CreatedAt = SavedAt, UpdatedAt = SavedAt,
        };
        db.Spaces.Add(space);
        if (team != null)
        {
            db.TeamSpaces.Add(new TeamSpace { Team = team, Space = space });
        }
        await db.SaveChangesAsync();
        return space;
    }

    private static async Task<(Resource Resource, ResourceConfig Config)> SeedResource(
        AppDbContext db, Team team, Space space, User user, string name = "Meeting room", bool isActive = true,
        Resource? parent = null, ResourceTemplate? template = null)
    {
        var resource = new Resource
        {
            TeamId = team.Id, SpaceId = space.Id, ParentResourceId = parent?.Id,
            Name = name, NameKey = name.ToUpperInvariant(), Slug = name.ToLowerInvariant().Replace(' ', '-'),
            Category = "room", Capacity = 12, IsActive = isActive, PublicAccess = parent == null ? "availability" : null,
            CreatedAt = SavedAt, UpdatedAt = SavedAt, UpdatedByUserId = user.Id,
        };
        var config = new ResourceConfig
        {
            Resource = resource, Version = 1, SourceTemplateId = template?.Id,
            FormSchemaVersion = 1, FormJson = FormJson, DetailsSchemaVersion = 1, DetailsJson = DetailsJson,
            CreatedByUserId = user.Id, PublishedByUserId = user.Id,
            CreatedAt = SavedAt, UpdatedAt = SavedAt, PublishedAt = SavedAt,
        };
        db.Resources.Add(resource);
        db.ResourceConfigs.Add(config);
        await db.SaveChangesAsync();
        return (resource, config);
    }

    private static async Task<ReservationSeries> SeedReservation(
        AppDbContext db, Resource resource, ResourceConfig config, User user)
    {
        var reservation = new ReservationSeries
        {
            ResourceId = resource.Id, ResourceConfigId = config.Id, RequesterUserId = user.Id,
            FormResponsesJson = "{\"name\":\"Requester\"}", TimeZoneId = "America/Los_Angeles",
            SubmissionKey = Guid.NewGuid(), CreatedByUserId = user.Id, CreatedAt = SavedAt,
        };
        db.ReservationSeries.Add(reservation);
        await db.SaveChangesAsync();
        return reservation;
    }

    private static TeamResourcesController CreateController(
        AppDbContext db, string? iamId = "member-iam", bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new TeamResourcesController(db, new TeamAccessService(db), new TeamResourceService(db))
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

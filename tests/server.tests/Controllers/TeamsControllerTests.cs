using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Teams;
using Server.Services;

namespace Server.Tests.Controllers;

public class TeamsControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetTeams_returns_only_explicit_memberships_in_name_order_even_for_site_admins(bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "member-iam", Name = "Member", IsAdmin = isAdmin };
        db.TeamPermissions.AddRange(
            new TeamPermission { User = user, Team = new Team { Name = "Zoology", Slug = "zoology" }, Role = TeamRole.Viewer },
            new TeamPermission { User = user, Team = new Team { Name = "Biology", Slug = "biology" }, Role = TeamRole.Editor },
            new TeamPermission { User = user, Team = new Team { Name = "Physics", Slug = "physics" }, Role = TeamRole.Admin },
            new TeamPermission { User = new User { IamId = "other-iam", Name = "Other" },
                Team = new Team { Name = "Chemistry", Slug = "chemistry" }, Role = TeamRole.Admin });
        db.Teams.Add(new Team { Name = "Unassigned", Slug = "unassigned" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).GetTeams();

        var teams = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<List<TeamSummaryResponse>>().Subject;
        teams.Select(team => team.Slug).Should().Equal("biology", "physics", "zoology");
        teams.Select(team => team.Role).Should().Equal(TeamRole.Editor, TeamRole.Admin, TeamRole.Viewer);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(teams, TeamJsonOptions()));
        json.RootElement.EnumerateArray().Select(team => team.GetProperty("role").GetString())
            .Should().Equal("editor", "admin", "viewer");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(TeamRole.Admin)]
    [InlineData(TeamRole.Editor)]
    [InlineData(TeamRole.Viewer)]
    public async Task GetTeam_returns_the_matching_membership_for_every_team_role(TeamRole role)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.TeamPermissions.Add(new TeamPermission
        {
            User = new User { IamId = "member-iam", Name = "Member" },
            Team = new Team { Name = "Biology", Slug = "biology" },
            Role = role,
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db).GetTeam("biology");

        var access = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<TeamAccessResponse>().Subject;
        access.Team.Slug.Should().Be("biology");
        access.Team.Name.Should().Be("Biology");
        access.Team.Role.Should().BeNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(access, TeamJsonOptions()));
        json.RootElement.GetProperty("team").TryGetProperty("role", out _).Should().BeFalse();
        access.Role.Should().Be(role);
        access.IsSiteAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task GetTeam_allows_site_admin_without_creating_a_membership()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "member-iam", Name = "Admin", IsAdmin = true });
        db.Teams.Add(new Team { Name = "Biology", Slug = "biology" });
        await db.SaveChangesAsync();

        var result = await CreateController(db).GetTeam("biology");

        var access = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<TeamAccessResponse>().Subject;
        access.Role.Should().BeNull();
        access.IsSiteAdmin.Should().BeTrue();
        db.TeamPermissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, TeamRole.Viewer)]
    [InlineData(true, (TeamRole)999)]
    public async Task Inactive_users_and_unrecognized_roles_have_no_team_access(bool isActive, TeamRole role)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.TeamPermissions.Add(new TeamPermission
        {
            User = new User { IamId = "member-iam", Name = "Member", IsActive = isActive },
            Team = new Team { Name = "Biology", Slug = "biology" }, Role = role,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var teams = (await controller.GetTeams()).Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<List<TeamSummaryResponse>>().Subject;

        teams.Should().BeEmpty();
        (await controller.GetTeam("biology")).Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetTeam_returns_not_found_for_an_unknown_slug()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "member-iam", Name = "Admin", IsAdmin = true });
        await db.SaveChangesAsync();

        (await CreateController(db).GetTeam("missing")).Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Team_routes_require_authentication_and_slug_access_without_aliasing_the_list_route()
    {
        var controller = typeof(TeamsController);
        controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Should().NotBeEmpty();
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(route => route.Template)
            .Should().Equal("api/[controller]");
        controller.GetMethod(nameof(TeamsController.GetTeams))!.GetCustomAttribute<HttpGetAttribute>()!.Template
            .Should().BeNull();
        var landing = controller.GetMethod(nameof(TeamsController.GetTeam))!;
        landing.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("{teamSlug}");
        landing.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(AuthenticationHelper.TeamAccessPolicy);
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Member_list_is_scoped_sorted_and_includes_existing_viewers_and_inactive_users()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var team = new Team { Name = "Team A", Slug = "team-a" };
        db.TeamPermissions.AddRange(
            new TeamPermission { Team = team, User = new User { IamId = "1", Name = "Zoe" }, Role = TeamRole.Admin },
            new TeamPermission { Team = team, User = new User { IamId = "2", Name = "Amy", IsActive = false }, Role = TeamRole.Viewer },
            new TeamPermission { Team = new Team { Name = "Other", Slug = "other" },
                User = new User { IamId = "3", Name = "Other Member" }, Role = TeamRole.Editor });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var members = ReadValue(await CreateMembersController(db).GetMembers("team-a"));

        members.Select(member => member.Name).Should().Equal("Amy", "Zoe");
        members[0].Role.Should().Be(TeamRole.Viewer);
        members[0].IsActive.Should().BeFalse();
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await CreateMembersController(db).GetMembers("missing")).Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData("directory@example.test")]
    [InlineData("0000000001")]
    [InlineData("person1")]
    public async Task Member_search_matches_exact_directory_identifiers_and_exposes_team_specific_status(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = DirectoryPerson();
        person.IsActiveInIam = false;
        db.People.Add(person);
        var user = new User { IamId = person.IamId, Name = "Existing", IsActive = false };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Viewer,
        });
        db.Teams.Add(new Team { Name = "Team B", Slug = "team-b" });
        await db.SaveChangesAsync();

        var match = ReadValue(await CreateMembersController(db).SearchPeople("team-a", query)).Single();

        match.IamId.Should().Be(person.IamId);
        match.Name.Should().Be("Directory Person");
        match.Kerberos.Should().Be("person1");
        match.IsActive.Should().BeFalse();
        match.IsActiveInIam.Should().BeFalse();
        match.Role.Should().Be(TeamRole.Viewer);
        ReadValue(await CreateMembersController(db).SearchPeople("team-b", query)).Single().Role.Should().BeNull();
    }

    [Fact]
    public async Task Member_search_supports_people_without_users_and_limits_exact_matches()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        for (var index = 1; index <= 12; index++)
        {
            db.People.Add(new Person
            {
                IamId = $"{index:D10}", FullName = $"Person {index:D2}", Email = "shared@example.test", IsActiveInIam = true,
            });
        }
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);

        var matches = ReadValue(await controller.SearchPeople("team-a", " shared@example.test "));

        matches.Should().HaveCount(10);
        matches.Should().OnlyContain(person => person.IsActive && person.IsActiveInIam && person.Role == null);
        ReadValue(await controller.SearchPeople("team-a", "shared")).Should().BeEmpty();
        ReadValue(await controller.SearchPeople("team-a", "%")).Should().BeEmpty();
        (await controller.SearchPeople("team-a", " ")).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.SearchPeople("team-a", new string('a', 129))).Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(TeamRole.Admin)]
    [InlineData(TeamRole.Editor)]
    public async Task Add_member_creates_user_from_active_directory_data_and_assigns_only_the_requested_team(TeamRole role)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        db.People.Add(DirectoryPerson());
        await db.SaveChangesAsync();

        var member = ReadValue(await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = " 0000000001 ", Role = role }));

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        member.Id.Should().Be(user.Id);
        member.Name.Should().Be("Directory Person");
        member.Email.Should().Be("directory@example.test");
        member.Role.Should().Be(role);
        user.IsAdmin.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();
        user.UpdatedAt.Should().Be(user.CreatedAt);
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(role);
    }

    [Fact]
    public async Task Add_existing_user_preserves_profile_global_admin_and_other_team_role()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        var user = new User
        {
            IamId = "0000000001", Name = "Existing Name", Email = "existing@example.test", IsAdmin = true,
            CreatedAt = originalTime, UpdatedAt = originalTime, LastLoginAt = originalTime,
        };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Other", Slug = "other" }, Role = TeamRole.Viewer,
        });
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        db.People.Add(DirectoryPerson());
        await db.SaveChangesAsync();

        var member = ReadValue(await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = user.IamId, Role = TeamRole.Editor }));

        member.Name.Should().Be("Existing Name");
        member.Email.Should().Be("existing@example.test");
        user.IsAdmin.Should().BeTrue();
        user.UpdatedAt.Should().Be(originalTime);
        user.LastLoginAt.Should().Be(originalTime);
        (await db.TeamPermissions.SingleAsync(permission => permission.Team.Slug == "other")).Role.Should().Be(TeamRole.Viewer);
        var duplicate = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = user.IamId, Role = TeamRole.Admin });
        duplicate.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.TeamPermissions.SingleAsync(permission => permission.Team.Slug == "team-a")).Role.Should().Be(TeamRole.Editor);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Add_member_rechecks_inactive_directory_and_application_users(bool activeInIam, bool activeUser)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        var person = DirectoryPerson();
        db.People.Add(person);
        db.Users.Add(new User { IamId = person.IamId, Name = "Existing", IsActive = activeUser });
        await db.SaveChangesAsync();
        await CreateMembersController(db).SearchPeople("team-a", person.IamId);
        person.IsActiveInIam = activeInIam;
        await db.SaveChangesAsync();

        var result = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = person.IamId, Role = TeamRole.Admin });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        db.TeamPermissions.Should().BeEmpty();
        (await db.Users.SingleAsync()).IsActive.Should().Be(activeUser);
    }

    [Fact]
    public async Task Add_member_rejects_missing_directory_record_unknown_team_and_invalid_iam()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);
        var request = new AddTeamMemberRequest { IamId = "0000000001", Role = TeamRole.Editor };

        (await controller.AddMember("team-a", request)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.AddMember("missing", request)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = " ", Role = TeamRole.Editor }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = "12345678901", Role = TeamRole.Editor }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        db.Users.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TeamRole.Viewer)]
    [InlineData((TeamRole)0)]
    [InlineData((TeamRole)999)]
    public async Task Add_and_update_reject_missing_legacy_and_undefined_roles(TeamRole? role)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = CreateMembersController(db);

        (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = "0000000001", Role = role }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateRole("team-a", 1, new UpdateTeamMemberRoleRequest { Role = role }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        db.TeamPermissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData("\"1\"")]
    [InlineData("\"999\"")]
    [InlineData("\"owner\"")]
    public void Team_role_json_rejects_numeric_tokens_numeric_strings_and_unknown_names(string roleJson)
    {
        var options = TeamJsonOptions();
        var json = "{\"iamId\":\"0000000001\",\"role\":" + roleJson + "}";

        var deserialize = () => JsonSerializer.Deserialize<AddTeamMemberRequest>(json, options);

        deserialize.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(TeamRole.Admin, "admin")]
    [InlineData(TeamRole.Editor, "editor")]
    [InlineData(TeamRole.Viewer, "viewer")]
    public void Team_role_json_preserves_lowercase_names_including_existing_viewers(TeamRole role, string name)
    {
        var options = TeamJsonOptions();

        JsonSerializer.Serialize(role, options).Should().Be($"\"{name}\"");
        JsonSerializer.Deserialize<TeamRole>($"\"{name}\"", options).Should().Be(role);
    }

    [Fact]
    public async Task Updating_and_removing_members_preserves_users_and_other_team_memberships()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Target", IsAdmin = true };
        var targetPermission = new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Viewer,
        };
        db.TeamPermissions.AddRange(targetPermission, new TeamPermission
        {
            User = user, Team = new Team { Name = "Other", Slug = "other" }, Role = TeamRole.Admin,
        });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);

        var updated = ReadValue(await controller.UpdateRole("team-a", user.Id,
            new UpdateTeamMemberRoleRequest { Role = TeamRole.Editor }));

        updated.Role.Should().Be(TeamRole.Editor);
        user.IsAdmin.Should().BeTrue();
        (await controller.RemoveMember("team-a", user.Id)).Should().BeOfType<NoContentResult>();
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync()).IsAdmin.Should().BeTrue();
        var remaining = await db.TeamPermissions.Include(permission => permission.Team).SingleAsync();
        remaining.Team.Slug.Should().Be("other");
        remaining.Role.Should().Be(TeamRole.Admin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Self_role_changes_and_removal_are_blocked_including_site_admins(bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Self", IsAdmin = isAdmin };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Admin,
        });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db, " 0000000001 ");

        (await controller.UpdateRole("team-a", user.Id, new UpdateTeamMemberRoleRequest { Role = TeamRole.Editor }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateRole("team-a", user.Id, new UpdateTeamMemberRoleRequest { Role = TeamRole.Admin }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.RemoveMember("team-a", user.Id)).Should().BeOfType<BadRequestObjectResult>();
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Admin);
    }

    [Fact]
    public async Task Membership_mutations_cannot_target_a_user_from_another_team_or_missing_identity()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Other Member" };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Other", Slug = "other" }, Role = TeamRole.Editor,
        });
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);
        var update = new UpdateTeamMemberRoleRequest { Role = TeamRole.Admin };

        (await controller.UpdateRole("team-a", user.Id, update)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.RemoveMember("team-a", user.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await CreateMembersController(db, null).UpdateRole("other", user.Id, update)).Result.Should().BeOfType<ForbidResult>();
        (await CreateMembersController(db, null).RemoveMember("other", user.Id)).Should().BeOfType<ForbidResult>();
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Concurrent_first_login_or_add_preserves_the_winning_user_and_membership(bool activeUser, bool addMembership)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"TeamMemberRace_{Guid.NewGuid():N}").Options;
        using var db = new MemberInsertRaceDbContext(options)
        {
            InsertConcurrentUser = true, ConcurrentUserActive = activeUser, InsertConcurrentMembership = addMembership,
        };
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        db.People.Add(DirectoryPerson());
        await db.SaveChangesAsync();
        db.FailNextMembershipSave = true;

        var result = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000001", Role = TeamRole.Admin });

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Concurrent Login");
        user.Email.Should().Be("login@example.test");
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().Be(activeUser);
        user.LastLoginAt.Should().NotBeNull();
        if (activeUser && !addMembership)
        {
            ReadValue(result).Id.Should().Be(user.Id);
            (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Admin);
        }
        else
        {
            result.Result.Should().BeOfType<ConflictObjectResult>();
            if (addMembership)
            {
                (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
            }
            else
            {
                db.TeamPermissions.Should().BeEmpty();
            }
        }
    }

    [Fact]
    public async Task Concurrent_add_for_an_existing_user_conflicts_without_replacing_the_winning_role()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"MemberRace_{Guid.NewGuid():N}").Options;
        using var db = new MemberInsertRaceDbContext(options) { InsertConcurrentMembership = true };
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        db.People.Add(DirectoryPerson());
        db.Users.Add(new User { IamId = "0000000001", Name = "Existing" });
        await db.SaveChangesAsync();
        db.FailNextMembershipSave = true;

        var result = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000001", Role = TeamRole.Admin });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
    }

    [Fact]
    public async Task Unrelated_member_save_failures_are_not_reported_as_success_or_duplicate_membership()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"MemberFailure_{Guid.NewGuid():N}").Options;
        using var db = new MemberInsertRaceDbContext(options);
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        db.People.Add(DirectoryPerson());
        await db.SaveChangesAsync();
        db.FailNextMembershipSave = true;

        var add = () => CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000001", Role = TeamRole.Admin });

        await add.Should().ThrowAsync<DbUpdateException>();
        db.Users.Should().BeEmpty();
        db.TeamPermissions.Should().BeEmpty();
    }

    [Fact]
    public void Every_member_endpoint_requires_the_team_admin_policy_and_mutations_validate_antiforgery()
    {
        var controller = typeof(TeamMembersController);
        controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(AuthenticationHelper.TeamAdminPolicy);
        controller.GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
        controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(route => route.Template)
            .Should().Equal("api/teams/{teamSlug}/members");
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        foreach (var name in new[] { nameof(TeamMembersController.AddMember), nameof(TeamMembersController.UpdateRole), nameof(TeamMembersController.RemoveMember) })
        {
            controller.GetMethod(name)!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        }
    }

    private static T ReadValue<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static Person DirectoryPerson() => new()
    {
        IamId = "0000000001", FullName = "Directory Person", Email = "directory@example.test", UserId = "person1", IsActiveInIam = true,
    };

    private static JsonSerializerOptions TeamJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<TeamRole>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static TeamMembersController CreateMembersController(AppDbContext db, string? iamId = "acting-admin")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "different-identity-id") };
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new TeamMembersController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private sealed class MemberInsertRaceDbContext : AppDbContext
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public bool FailNextMembershipSave { get; set; }
        public bool InsertConcurrentUser { get; init; }
        public bool InsertConcurrentMembership { get; init; }
        public bool ConcurrentUserActive { get; init; } = true;

        public MemberInsertRaceDbContext(DbContextOptions<AppDbContext> options) : base(options) => _options = options;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextMembershipSave)
            {
                FailNextMembershipSave = false;
                var permission = ChangeTracker.Entries<TeamPermission>().Single(entry => entry.State == EntityState.Added).Entity;
                using var concurrentDb = new AppDbContext(_options);
                var concurrentUser = await concurrentDb.Users.SingleOrDefaultAsync(user => user.IamId == permission.User.IamId, cancellationToken);
                if (InsertConcurrentUser)
                {
                    concurrentUser = new User
                    {
                        IamId = permission.User.IamId, Name = "Concurrent Login", Email = "login@example.test",
                        IsActive = ConcurrentUserActive, IsAdmin = true, LastLoginAt = DateTimeOffset.UtcNow,
                    };
                    concurrentDb.Users.Add(concurrentUser);
                }
                if (InsertConcurrentMembership)
                {
                    concurrentDb.TeamPermissions.Add(new TeamPermission
                    {
                        TeamId = permission.TeamId, User = concurrentUser!, Role = TeamRole.Editor,
                    });
                }
                await concurrentDb.SaveChangesAsync(cancellationToken);
                throw new DbUpdateException("Simulated concurrent membership insertion.");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private static TeamsController CreateController(AppDbContext db)
        => new(new TeamAccessService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "different-identity-id"),
                        new Claim("ucdPersonIAMID", "member-iam"),
                    ], "Test")),
                },
            },
        };
}

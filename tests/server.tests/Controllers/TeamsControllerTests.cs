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
using Server.Models.Directory;
using Server.Models.Teams;
using Server.Services;

namespace Server.Tests.Controllers;

public class TeamsControllerTests
{
    private readonly List<DirectoryPerson> _people = [];

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
        teams.SelectMany(team => team.Roles).Should().Equal(TeamRole.Editor, TeamRole.Admin, TeamRole.Viewer);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(teams, TeamJsonOptions()));
        json.RootElement.EnumerateArray().SelectMany(team => team.GetProperty("roles").EnumerateArray())
            .Select(role => role.GetString())
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
        access.Team.Roles.Should().Equal(role);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(access, TeamJsonOptions()));
        json.RootElement.GetProperty("team").GetProperty("roles").GetArrayLength().Should().Be(1);
        access.Roles.Should().Equal(role);
        access.IsSiteAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Team_queries_group_multiple_roles_into_one_team_and_one_member()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var team = new Team { Name = "Biology", Slug = "biology" };
        var user = new User
        {
            IamId = "member-iam", Name = "Member", Email = "member@example.test", Kerberos = "member",
        };
        db.TeamPermissions.AddRange(
            new TeamPermission { User = user, Team = team, Role = TeamRole.Editor },
            new TeamPermission { User = user, Team = team, Role = TeamRole.Admin });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var summary = ReadValue(await CreateController(db).GetTeams()).Should().ContainSingle().Which;
        summary.Roles.Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        var access = ReadValue(await CreateController(db).GetTeam("biology"));
        access.Roles.Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        access.Team.Roles.Should().BeEquivalentTo(access.Roles);
        var member = ReadValue(await CreateMembersController(db).GetMembers("biology")).Should().ContainSingle().Which;
        member.Roles.Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        var match = ReadValue(await CreateMembersController(db).SearchPeople("biology", user.IamId))
            .Should().ContainSingle().Which;
        match.Roles.Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        db.ChangeTracker.Entries().Should().BeEmpty();
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
        access.Roles.Should().BeEmpty();
        access.Team.Roles.Should().BeEmpty();
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
        members[0].Roles.Should().Equal(TeamRole.Viewer);
        members[0].IsActive.Should().BeFalse();
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await CreateMembersController(db).GetMembers("missing")).Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Theory]
    [InlineData("directory@example.test")]
    [InlineData("person1")]
    public async Task Member_search_matches_exact_directory_identifiers_and_exposes_team_specific_status(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = CreatePerson();
        _people.Add(person);
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
        match.Roles.Should().Equal(TeamRole.Viewer);
        ReadValue(await CreateMembersController(db).SearchPeople("team-b", query)).Single().Roles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0000000001")]
    [InlineData("existing@example.test")]
    [InlineData("existingkerb")]
    public async Task Member_search_returns_existing_users_before_calling_the_directory(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.TeamPermissions.Add(new TeamPermission
        {
            Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Editor,
            User = new User
            {
                IamId = "0000000001", Name = "Existing User", Email = "existing@example.test", IsActive = false,
                Kerberos = "existingkerb",
            },
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rosetta = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable.") };

        var result = await CreateMembersController(db, rosetta: rosetta).SearchPeople("team-a", query);

        var person = ReadValue(result).Should().ContainSingle().Which;
        person.Name.Should().Be("Existing User");
        person.Email.Should().Be("existing@example.test");
        person.IsActive.Should().BeFalse();
        person.Kerberos.Should().Be("existingkerb");
        person.Roles.Should().Equal(TeamRole.Editor);
        rosetta.LookupCalls.Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Member_search_supports_people_without_users_and_limits_exact_matches()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        for (var index = 1; index <= 12; index++)
        {
            _people.Add(new DirectoryPerson
            {
                IamId = $"{index:D10}", Name = $"Person {index:D2}", Email = "shared@example.test", Kerberos = $"person{index}",
            });
        }
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);

        var matches = ReadValue(await controller.SearchPeople("team-a", " shared@example.test "));

        matches.Should().HaveCount(10);
        matches.Should().OnlyContain(person => person.IsActive && person.Roles.Count == 0);
        ReadValue(await controller.SearchPeople("team-a", "shared")).Should().BeEmpty();
        ReadValue(await controller.SearchPeople("team-a", "%")).Should().BeEmpty();
        (await controller.SearchPeople("team-a", " ")).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.SearchPeople("team-a", new string('a', 129))).Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(null, "person1", "directory@example.test")]
    [InlineData("", "person1", "directory@example.test")]
    [InlineData("   ", "person1", "directory@example.test")]
    [InlineData("0000000001", null, "directory@example.test")]
    [InlineData("0000000001", "", "directory@example.test")]
    [InlineData("0000000001", "   ", "directory@example.test")]
    [InlineData("0000000001", "person1", null)]
    [InlineData("0000000001", "person1", "")]
    [InlineData("0000000001", "person1", "   ")]
    public async Task Member_search_excludes_people_missing_required_details(string? iamId, string? kerberos, string? email)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        _people.Add(new DirectoryPerson
        {
            IamId = iamId!, Name = "Incomplete Person", Kerberos = kerberos, Email = email,
        });

        var query = string.IsNullOrWhiteSpace(email) ? "person1" : "directory@example.test";
        var result = await CreateMembersController(db).SearchPeople("team-a", query);

        ReadValue(result).Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "existingkerb")]
    [InlineData("   ", "existingkerb")]
    [InlineData("existing@example.test", null)]
    [InlineData("existing@example.test", "   ")]
    public async Task Member_search_uses_complete_directory_details_when_the_saved_user_is_incomplete(string? email, string? kerberos)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing User", Email = email, Kerberos = kerberos,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateMembersController(db).SearchPeople("team-a", "0000000001");

        var match = ReadValue(result).Should().ContainSingle().Which;
        match.IamId.Should().Be("0000000001");
        match.Name.Should().Be("Directory Person");
        match.Email.Should().Be("directory@example.test");
        match.Kerberos.Should().Be("person1");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(TeamRole.Admin)]
    [InlineData(TeamRole.Editor)]
    public async Task Add_member_creates_user_from_complete_directory_data_and_assigns_only_the_requested_team(TeamRole role)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();

        var member = ReadValue(await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = " 0000000001 ", Roles = [role] }));

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        member.Id.Should().Be(user.Id);
        member.Name.Should().Be("Directory Person");
        member.Email.Should().Be("directory@example.test");
        member.Roles.Should().Equal(role);
        user.Kerberos.Should().Be("person1");
        user.IsAdmin.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();
        user.UpdatedAt.Should().Be(user.CreatedAt);
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(role);
    }

    [Theory]
    [InlineData(null, "existing@example.test")]
    [InlineData("existingkerb", "existing@example.test")]
    [InlineData("existingkerb", null)]
    [InlineData("existingkerb", "")]
    [InlineData("   ", "   ")]
    public async Task Add_existing_user_populates_missing_required_details_and_preserves_profile_global_admin_and_other_team_role(
        string? existingKerberos, string? existingEmail)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        var user = new User
        {
            IamId = "0000000001", Name = "Existing Name", Email = existingEmail, IsAdmin = true,
            Kerberos = existingKerberos,
            CreatedAt = originalTime, UpdatedAt = originalTime, LastLoginAt = originalTime,
        };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Other", Slug = "other" }, Role = TeamRole.Viewer,
        });
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();

        var member = ReadValue(await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = user.IamId, Roles = [TeamRole.Editor] }));

        var expectedEmail = string.IsNullOrWhiteSpace(existingEmail) ? "directory@example.test" : existingEmail;
        var expectedKerberos = string.IsNullOrWhiteSpace(existingKerberos) ? "person1" : existingKerberos;
        member.Name.Should().Be("Existing Name");
        member.Email.Should().Be(expectedEmail);
        user.IsAdmin.Should().BeTrue();
        user.Kerberos.Should().Be(expectedKerberos);
        var savedUser = await db.Users.AsNoTracking().SingleAsync();
        savedUser.Email.Should().Be(expectedEmail);
        savedUser.Kerberos.Should().Be(expectedKerberos);
        if (string.IsNullOrWhiteSpace(existingKerberos) || string.IsNullOrWhiteSpace(existingEmail))
        {
            user.UpdatedAt.Should().BeAfter(originalTime);
        }
        else
        {
            user.UpdatedAt.Should().Be(originalTime);
        }
        user.LastLoginAt.Should().Be(originalTime);
        (await db.TeamPermissions.SingleAsync(permission => permission.Team.Slug == "other")).Role.Should().Be(TeamRole.Viewer);
        var duplicate = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = user.IamId, Roles = [TeamRole.Admin] });
        duplicate.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.TeamPermissions.SingleAsync(permission => permission.Team.Slug == "team-a")).Role.Should().Be(TeamRole.Editor);
    }

    [Fact]
    public async Task Add_member_rechecks_application_activity_after_search()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        var person = CreatePerson();
        _people.Add(person);
        var user = new User
        {
            IamId = person.IamId, Name = "Existing", Email = person.Email, Kerberos = person.Kerberos,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        ReadValue(await CreateMembersController(db).SearchPeople("team-a", person.IamId)).Should().ContainSingle();
        user.IsActive = false;
        await db.SaveChangesAsync();

        var result = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = person.IamId, Roles = [TeamRole.Admin] });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        db.TeamPermissions.Should().BeEmpty();
        (await db.Users.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Add_member_rechecks_required_directory_details_after_search()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        await db.SaveChangesAsync();
        var person = CreatePerson();
        _people.Add(person);
        var controller = CreateMembersController(db);
        var match = ReadValue(await controller.SearchPeople("team-a", "person1")).Should().ContainSingle().Which;
        person.Email = null;

        var result = await controller.AddMember("team-a", new AddTeamMemberRequest
        {
            IamId = match.IamId, Roles = [TeamRole.Admin],
        });

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        db.Users.Should().BeEmpty();
        db.TeamPermissions.Should().BeEmpty();
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "person1", "directory@example.test")]
    [InlineData("", "person1", "directory@example.test")]
    [InlineData("   ", "person1", "directory@example.test")]
    [InlineData("0000000001", null, "directory@example.test")]
    [InlineData("0000000001", "", "directory@example.test")]
    [InlineData("0000000001", "   ", "directory@example.test")]
    [InlineData("0000000001", "person1", null)]
    [InlineData("0000000001", "person1", "")]
    [InlineData("0000000001", "person1", "   ")]
    public async Task Add_member_rejects_incomplete_directory_details_for_new_and_existing_users(
        string? iamId, string? kerberos, string? email)
    {
        _people.Add(new DirectoryPerson
        {
            IamId = iamId!, Name = "Incomplete Person", Kerberos = kerberos, Email = email,
        });
        foreach (var hasExistingUser in new[] { false, true })
        {
            using var db = TestDbContextFactory.CreateInMemory();
            db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
            if (hasExistingUser)
            {
                db.Users.Add(new User { IamId = "0000000001", Name = "Existing User" });
            }
            await db.SaveChangesAsync();
            var rosetta = new FakeRosettaService(db);
            var controller = CreateMembersController(db, rosetta: rosetta);

            var result = await controller.AddMember("team-a", new AddTeamMemberRequest
            {
                IamId = "0000000001", Roles = [TeamRole.Admin],
            });

            result.Result.Should().BeOfType<NotFoundObjectResult>();
            rosetta.LookupCalls.Should().Equal("0000000001");
            db.TeamPermissions.Should().BeEmpty();
            (await db.Users.CountAsync()).Should().Be(hasExistingUser ? 1 : 0);
            db.ChangeTracker.HasChanges().Should().BeFalse();
        }
    }

    [Fact]
    public async Task Add_member_does_not_create_a_user_or_membership_when_the_directory_request_fails()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        await db.SaveChangesAsync();
        var rosetta = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable.") };
        var add = () => CreateMembersController(db, rosetta: rosetta).AddMember("team-a", new AddTeamMemberRequest
        {
            IamId = "0000000001", Roles = [TeamRole.Admin],
        });

        await add.Should().ThrowAsync<HttpRequestException>();

        db.Users.Should().BeEmpty();
        db.TeamPermissions.Should().BeEmpty();
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task Add_member_rejects_missing_directory_record_unknown_team_and_invalid_iam()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);
        var request = new AddTeamMemberRequest { IamId = "0000000001", Roles = [TeamRole.Editor] };

        (await controller.AddMember("team-a", request)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.AddMember("missing", request)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = " ", Roles = [TeamRole.Editor] }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = "12345678901", Roles = [TeamRole.Editor] }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        db.Users.Should().BeEmpty();
    }

    [Theory]
    [InlineData((TeamRole)0)]
    [InlineData((TeamRole)999)]
    public async Task Add_and_update_reject_undefined_roles(TeamRole role)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = CreateMembersController(db);

        (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = "0000000001", Roles = [role] }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateRoles("team-a", 1, new UpdateTeamMemberRolesRequest { Roles = [role] }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        db.TeamPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_and_update_reject_missing_empty_and_duplicate_roles_without_changing_membership()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Member" };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Editor,
        });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);

        foreach (var roles in new List<TeamRole>?[] { null, [], [TeamRole.Admin, TeamRole.Admin] })
        {
            (await controller.AddMember("team-a", new AddTeamMemberRequest { IamId = user.IamId, Roles = roles }))
                .Result.Should().BeOfType<BadRequestObjectResult>();
            (await controller.UpdateRoles("team-a", user.Id, new UpdateTeamMemberRolesRequest { Roles = roles }))
                .Result.Should().BeOfType<BadRequestObjectResult>();
        }

        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
    }

    [Fact]
    public async Task Add_member_assigns_multiple_roles_and_rejects_duplicate_membership()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);
        var request = new AddTeamMemberRequest { IamId = "0000000001", Roles = [TeamRole.Admin, TeamRole.Editor] };

        var member = ReadValue(await controller.AddMember("team-a", request));

        member.Roles.Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        db.Users.Should().ContainSingle();
        (await db.TeamPermissions.Select(permission => permission.Role).ToListAsync())
            .Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        (await controller.AddMember("team-a", request)).Result.Should().BeOfType<ConflictObjectResult>();
        db.TeamPermissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Legacy_viewer_can_be_retained_or_removed_but_cannot_be_newly_assigned()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Viewer" };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Viewer,
        });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db);

        var retained = ReadValue(await controller.UpdateRoles("team-a", user.Id,
            new UpdateTeamMemberRolesRequest { Roles = [TeamRole.Viewer, TeamRole.Editor] }));
        retained.Roles.Should().BeEquivalentTo(new[] { TeamRole.Viewer, TeamRole.Editor });
        var removed = ReadValue(await controller.UpdateRoles("team-a", user.Id,
            new UpdateTeamMemberRolesRequest { Roles = [TeamRole.Editor] }));
        removed.Roles.Should().Equal(TeamRole.Editor);

        (await controller.UpdateRoles("team-a", user.Id,
            new UpdateTeamMemberRolesRequest { Roles = [TeamRole.Viewer, TeamRole.Editor] }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000002", Roles = [TeamRole.Viewer] }))
            .Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
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
        var json = "{\"iamId\":\"0000000001\",\"roles\":[" + roleJson + "]}";

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Updating_and_removing_other_members_preserves_users_and_other_team_memberships(bool confirmSelfAccessChange)
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

        var updated = ReadValue(await controller.UpdateRoles("team-a", user.Id,
            new UpdateTeamMemberRolesRequest
            {
                Roles = [TeamRole.Admin, TeamRole.Editor], ConfirmSelfAccessChange = confirmSelfAccessChange,
            }));

        updated.Roles.Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        db.TeamPermissions.Count(permission => permission.Team.Slug == "team-a").Should().Be(2);
        ReadValue(await controller.UpdateRoles("team-a", user.Id, new UpdateTeamMemberRolesRequest
        {
            Roles = [TeamRole.Editor], ConfirmSelfAccessChange = confirmSelfAccessChange,
        })).Roles.Should().Equal(TeamRole.Editor);
        user.IsAdmin.Should().BeTrue();
        (await controller.RemoveMember("team-a", user.Id, confirmSelfAccessChange)).Should().BeOfType<NoContentResult>();
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync()).IsAdmin.Should().BeTrue();
        var remaining = await db.TeamPermissions.Include(permission => permission.Team).SingleAsync();
        remaining.Team.Slug.Should().Be("other");
        remaining.Role.Should().Be(TeamRole.Admin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Self_role_changes_can_add_and_remove_roles_without_confirmation_when_retaining_admin(bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Self", IsAdmin = isAdmin };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Admin,
        });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db, user.IamId);

        ReadValue(await controller.UpdateRoles("team-a", user.Id, new UpdateTeamMemberRolesRequest
        {
            Roles = [TeamRole.Admin, TeamRole.Editor],
        })).Roles.Should().Equal(TeamRole.Admin, TeamRole.Editor);
        ReadValue(await controller.UpdateRoles("team-a", user.Id, new UpdateTeamMemberRolesRequest
        {
            Roles = [TeamRole.Admin],
        })).Roles.Should().Equal(TeamRole.Admin);
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Admin);
    }

    [Fact]
    public async Task Site_admin_can_add_own_admin_role_without_confirmation()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "0000000001", Name = "Self", IsAdmin = true };
        db.TeamPermissions.Add(new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Editor,
        });
        await db.SaveChangesAsync();

        ReadValue(await CreateMembersController(db, user.IamId).UpdateRoles("team-a", user.Id,
            new UpdateTeamMemberRolesRequest { Roles = [TeamRole.Admin, TeamRole.Editor] }))
            .Roles.Should().Equal(TeamRole.Admin, TeamRole.Editor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_own_admin_role_requires_confirmation_for_normalized_effective_identity(bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "Member1", Name = "Self", IsAdmin = isAdmin };
        var team = new Team { Name = "Team A", Slug = "team-a" };
        db.TeamPermissions.AddRange(
            new TeamPermission { User = user, Team = team, Role = TeamRole.Admin },
            new TeamPermission { User = user, Team = team, Role = TeamRole.Editor });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db, " member1 ");

        var unconfirmed = await controller.UpdateRoles("team-a", user.Id,
            new UpdateTeamMemberRolesRequest { Roles = [TeamRole.Editor] });

        unconfirmed.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.TeamPermissions.Select(permission => permission.Role).ToListAsync())
            .Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
        db.ChangeTracker.HasChanges().Should().BeFalse();

        ReadValue(await controller.UpdateRoles("team-a", user.Id, new UpdateTeamMemberRolesRequest
        {
            Roles = [TeamRole.Editor], ConfirmSelfAccessChange = true,
        })).Roles.Should().Equal(TeamRole.Editor);
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
        user.IsAdmin.Should().Be(isAdmin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_own_membership_requires_confirmation_and_preserves_other_teams(bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = "Member1", Name = "Self", IsAdmin = isAdmin };
        var team = new Team { Name = "Team A", Slug = "team-a" };
        db.TeamPermissions.AddRange(
            new TeamPermission { User = user, Team = team, Role = TeamRole.Admin },
            new TeamPermission { User = user, Team = team, Role = TeamRole.Editor },
            new TeamPermission
            {
                User = user, Team = new Team { Name = "Other", Slug = "other" }, Role = TeamRole.Admin,
            });
        await db.SaveChangesAsync();
        var controller = CreateMembersController(db, " member1 ");

        (await controller.RemoveMember("team-a", user.Id)).Should().BeOfType<BadRequestObjectResult>();
        db.TeamPermissions.Should().HaveCount(3);
        db.ChangeTracker.HasChanges().Should().BeFalse();

        (await controller.RemoveMember("team-a", user.Id, confirmSelfAccessChange: true))
            .Should().BeOfType<NoContentResult>();
        db.ChangeTracker.Clear();
        var remaining = await db.TeamPermissions.Include(permission => permission.Team).SingleAsync();
        remaining.Team.Slug.Should().Be("other");
        (await db.Users.SingleAsync()).IsAdmin.Should().Be(isAdmin);
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
        var update = new UpdateTeamMemberRolesRequest { Roles = [TeamRole.Admin], ConfirmSelfAccessChange = true };

        (await controller.UpdateRoles("team-a", user.Id, update)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.RemoveMember("team-a", user.Id)).Should().BeOfType<NotFoundObjectResult>();
        (await CreateMembersController(db, null).UpdateRoles("other", user.Id, update)).Result.Should().BeOfType<ForbidResult>();
        (await CreateMembersController(db, null).RemoveMember("other", user.Id, confirmSelfAccessChange: true))
            .Should().BeOfType<ForbidResult>();
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
    }

    [Theory]
    [InlineData(true, false, null, "login@example.test")]
    [InlineData(true, false, "existingkerb", "login@example.test")]
    [InlineData(true, false, "existingkerb", null)]
    [InlineData(true, false, "   ", "   ")]
    [InlineData(false, false, null, null)]
    [InlineData(true, true, null, null)]
    public async Task Concurrent_first_login_or_add_preserves_the_winning_user_and_membership(
        bool activeUser, bool addMembership, string? existingKerberos, string? existingEmail)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"TeamMemberRace_{Guid.NewGuid():N}").Options;
        using var db = new MemberInsertRaceDbContext(options)
        {
            InsertConcurrentUser = true, ConcurrentUserActive = activeUser, InsertConcurrentMembership = addMembership,
            ConcurrentUserKerberos = existingKerberos, ConcurrentUserEmail = existingEmail,
        };
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();
        db.FailNextMembershipSave = true;

        var result = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000001", Roles = [TeamRole.Admin, TeamRole.Editor] });

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Concurrent Login");
        user.Email.Should().Be(activeUser && !addMembership && string.IsNullOrWhiteSpace(existingEmail)
            ? "directory@example.test" : existingEmail);
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().Be(activeUser);
        user.LastLoginAt.Should().NotBeNull();
        user.Kerberos.Should().Be(activeUser && !addMembership && string.IsNullOrWhiteSpace(existingKerberos)
            ? "person1" : existingKerberos);
        if (activeUser && !addMembership)
        {
            ReadValue(result).Id.Should().Be(user.Id);
            (await db.TeamPermissions.Select(permission => permission.Role).ToListAsync())
                .Should().BeEquivalentTo(new[] { TeamRole.Admin, TeamRole.Editor });
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
        _people.Add(CreatePerson());
        db.Users.Add(new User { IamId = "0000000001", Name = "Existing" });
        await db.SaveChangesAsync();
        db.FailNextMembershipSave = true;

        var result = await CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000001", Roles = [TeamRole.Admin, TeamRole.Editor] });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Editor);
    }

    [Fact]
    public async Task Unrelated_member_save_failures_are_not_reported_as_success_or_duplicate_membership()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"MemberFailure_{Guid.NewGuid():N}").Options;
        using var db = new MemberInsertRaceDbContext(options);
        db.Teams.Add(new Team { Name = "Team A", Slug = "team-a" });
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();
        db.FailNextMembershipSave = true;

        var add = () => CreateMembersController(db).AddMember("team-a",
            new AddTeamMemberRequest { IamId = "0000000001", Roles = [TeamRole.Admin] });

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
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>(inherit: true).Should().NotBeNull();
        controller.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        foreach (var name in new[] { nameof(TeamMembersController.AddMember), nameof(TeamMembersController.UpdateRoles), nameof(TeamMembersController.RemoveMember) })
        {
            controller.GetMethod(name)!.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        }
        controller.GetMethod(nameof(TeamMembersController.AddMember))!
            .GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
        controller.GetMethod(nameof(TeamMembersController.UpdateRoles))!
            .GetCustomAttribute<HttpPutAttribute>()!.Template.Should().Be("{userId:int}/roles");
        controller.GetMethod(nameof(TeamMembersController.RemoveMember))!
            .GetCustomAttribute<HttpDeleteAttribute>()!.Template.Should().Be("{userId:int}");
    }

    private static T ReadValue<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static DirectoryPerson CreatePerson() => new()
    {
        IamId = "0000000001", Name = "Directory Person", Email = "directory@example.test", Kerberos = "person1",
    };

    private static JsonSerializerOptions TeamJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter<TeamRole>(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private TeamMembersController CreateMembersController(AppDbContext db, string? iamId = "acting-admin", FakeRosettaService? rosetta = null)
    {
        rosetta ??= new FakeRosettaService(db);
        rosetta.People.AddRange(_people);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "different-identity-id") };
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new TeamMembersController(db, rosetta)
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
        public string? ConcurrentUserKerberos { get; init; }
        public string? ConcurrentUserEmail { get; init; } = "login@example.test";

        public MemberInsertRaceDbContext(DbContextOptions<AppDbContext> options) : base(options) => _options = options;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextMembershipSave)
            {
                FailNextMembershipSave = false;
                var permission = ChangeTracker.Entries<TeamPermission>().First(entry => entry.State == EntityState.Added).Entity;
                using var concurrentDb = new AppDbContext(_options);
                var concurrentUser = await concurrentDb.Users.SingleOrDefaultAsync(user => user.IamId == permission.User.IamId, cancellationToken);
                if (InsertConcurrentUser)
                {
                    concurrentUser = new User
                    {
                        IamId = permission.User.IamId, Name = "Concurrent Login", Email = ConcurrentUserEmail,
                        IsActive = ConcurrentUserActive, IsAdmin = true, LastLoginAt = DateTimeOffset.UtcNow,
                        Kerberos = ConcurrentUserKerberos,
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

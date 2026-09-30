using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
            new TeamPermission { User = user, Team = new Team { Name = "Zoology", Slug = "zoology" }, Role = "viewer" },
            new TeamPermission { User = user, Team = new Team { Name = "Biology", Slug = "biology" }, Role = "editor" },
            new TeamPermission { User = new User { IamId = "other-iam", Name = "Other" },
                Team = new Team { Name = "Chemistry", Slug = "chemistry" }, Role = "admin" });
        db.Teams.Add(new Team { Name = "Unassigned", Slug = "unassigned" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).GetTeams();

        var teams = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<List<TeamSummaryResponse>>().Subject;
        teams.Select(team => team.Slug).Should().Equal("biology", "zoology");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("viewer")]
    public async Task GetTeam_returns_the_matching_membership_for_every_team_role(string role)
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
    [InlineData(false, "viewer")]
    [InlineData(true, "unexpected-role")]
    public async Task Inactive_users_and_unrecognized_roles_have_no_team_access(bool isActive, string role)
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

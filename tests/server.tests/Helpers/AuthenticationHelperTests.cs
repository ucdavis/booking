using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Services;

namespace Server.Tests.Helpers;

public class AuthenticationHelperTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public AuthenticationHelperTests()
    {
        _provider = CreateProvider();
        _scope = _provider.CreateScope();
    }

    private static ServiceProvider CreateProvider(bool local = false, Func<AppDbContext>? createDbContext = null,
        string? adminIamIds = null, string? environmentName = null, FakeRosettaService? rosetta = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Instance"] = "https://login.microsoftonline.com/",
            ["Auth:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["Auth:UseLocal"] = local.ToString(),
            ["DevelopmentData:AdminIamIds"] = adminIamIds,
        }).Build();
        var environment = new TestEnvironment
        {
            EnvironmentName = environmentName ?? (local ? Environments.Development : Environments.Production),
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddScoped(_ => createDbContext?.Invoke() ?? TestDbContextFactory.CreateInMemory());
        services.AddScoped<IUserService, UserService>();
        services.AddSingleton<IRosettaService>(rosetta ?? new FakeRosettaService());
        services.AddAuthenticationServices(configuration, environment);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Admin_access_endpoint_requires_the_site_admin_policy()
    {
        var controllerType = typeof(AdminController);
        var action = controllerType.GetMethod(nameof(AdminController.Access))!;

        controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Should().Contain(attribute => attribute.Policy == AuthenticationHelper.SiteAdminPolicy);
        controllerType.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controllerType.GetCustomAttribute<RouteAttribute>(inherit: true)!.Template.Should().Be("api/[controller]");
        action.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("access");
    }

    [Theory]
    [InlineData(false, true, TeamRole.Admin, "team-a", true)]
    [InlineData(false, true, TeamRole.Editor, "team-a", true)]
    [InlineData(false, true, TeamRole.Viewer, "team-a", true)]
    [InlineData(false, true, TeamRole.Admin, "team-b", false)]
    [InlineData(false, true, (TeamRole)999, "team-a", false)]
    [InlineData(false, true, null, "team-a", false)]
    [InlineData(false, false, TeamRole.Admin, "team-a", false)]
    [InlineData(true, true, null, "team-b", true)]
    [InlineData(true, true, TeamRole.Viewer, "team-b", true)]
    [InlineData(true, false, TeamRole.Admin, "team-a", false)]
    public async Task Team_policy_uses_active_database_access_for_the_requested_slug(
        bool isAdmin, bool isActive, TeamRole? role, string slug, bool expectedAccess)
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new Server.Core.Domain.User
        {
            IamId = "sandbox-10001", Name = "Member", IsAdmin = isAdmin, IsActive = isActive,
        };
        var team = new Server.Core.Domain.Team { Name = "Team A", Slug = "team-a" };
        db.Users.Add(user);
        db.Teams.AddRange(team, new Server.Core.Domain.Team { Name = "Team B", Slug = "team-b" });
        if (role != null)
        {
            db.TeamPermissions.Add(new Server.Core.Domain.TeamPermission { User = user, Team = team, Role = role.Value });
        }
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        var result = await AuthorizeTeam(_scope.ServiceProvider, principal, slug);

        result.Succeeded.Should().Be(expectedAccess);
    }

    [Fact]
    public async Task Team_policy_denies_missing_or_unauthenticated_identity_even_with_admin_claims()
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Server.Core.Domain.User { IamId = "sandbox-10001", Name = "Admin", IsAdmin = true });
        await db.SaveChangesAsync();
        var missingIam = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"));
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity(CreatePrincipal().Claims));

        (await AuthorizeTeam(_scope.ServiceProvider, missingIam, "team-a")).Succeeded.Should().BeFalse();
        (await AuthorizeTeam(_scope.ServiceProvider, unauthenticated, "team-a")).Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Team_policy_denies_missing_slug_even_for_site_admins(string? slug)
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Server.Core.Domain.User { IamId = "sandbox-10001", Name = "Admin", IsAdmin = true });
        await db.SaveChangesAsync();

        (await AuthorizeTeam(_scope.ServiceProvider, CreatePrincipal(), slug)).Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Team_policy_denies_missing_http_context(bool nullResource)
    {
        var result = await _scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(CreatePrincipal(), nullResource ? null : new object(), AuthenticationHelper.TeamAccessPolicy);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Team_policy_observes_membership_and_site_admin_revocation_without_a_new_login()
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new Server.Core.Domain.User { IamId = "sandbox-10001", Name = "Member" };
        var permission = new Server.Core.Domain.TeamPermission
        {
            User = user, Team = new Server.Core.Domain.Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Viewer,
        };
        db.TeamPermissions.Add(permission);
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();

        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a")).Succeeded.Should().BeTrue();
        db.TeamPermissions.Remove(permission);
        await db.SaveChangesAsync();
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a")).Succeeded.Should().BeFalse();
        user.IsAdmin = true;
        await db.SaveChangesAsync();
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a")).Succeeded.Should().BeTrue();
        user.IsAdmin = false;
        await db.SaveChangesAsync();
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a")).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Team_policy_allows_site_admins_to_reach_not_found_handling_for_unknown_slugs()
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Server.Core.Domain.User { IamId = "sandbox-10001", Name = "Admin", IsAdmin = true });
        await db.SaveChangesAsync();

        (await AuthorizeTeam(_scope.ServiceProvider, CreatePrincipal(), "missing")).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true, TeamRole.Admin, true)]
    [InlineData(false, true, TeamRole.Editor, false)]
    [InlineData(false, true, TeamRole.Viewer, false)]
    [InlineData(false, true, (TeamRole)999, false)]
    [InlineData(false, true, null, false)]
    [InlineData(false, false, TeamRole.Admin, false)]
    [InlineData(true, true, null, true)]
    [InlineData(true, true, TeamRole.Viewer, true)]
    [InlineData(true, false, TeamRole.Admin, false)]
    public async Task Team_admin_policy_requires_an_active_site_admin_or_an_admin_on_the_requested_team(
        bool isAdmin, bool isActive, TeamRole? role, bool expectedAccess)
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { IamId = "sandbox-10001", Name = "Member", IsAdmin = isAdmin, IsActive = isActive };
        var team = new Team { Name = "Team A", Slug = "team-a" };
        db.Users.Add(user);
        db.Teams.AddRange(team, new Team { Name = "Team B", Slug = "team-b" });
        if (role != null)
        {
            db.TeamPermissions.Add(new TeamPermission { User = user, Team = team, Role = role.Value });
        }
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().Be(expectedAccess);
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-b", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().Be(isAdmin && isActive);
    }

    [Fact]
    public async Task Team_admin_policy_requires_route_slug_authenticated_identity_and_http_context()
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User { IamId = "sandbox-10001", Name = "Admin", IsAdmin = true });
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();
        var services = _scope.ServiceProvider;
        var missingIam = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"));
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims));

        (await AuthorizeTeam(services, principal, null, AuthenticationHelper.TeamAdminPolicy)).Succeeded.Should().BeFalse();
        (await AuthorizeTeam(services, principal, " ", AuthenticationHelper.TeamAdminPolicy)).Succeeded.Should().BeFalse();
        (await AuthorizeTeam(services, missingIam, "team-a", AuthenticationHelper.TeamAdminPolicy)).Succeeded.Should().BeFalse();
        (await AuthorizeTeam(services, unauthenticated, "team-a", AuthenticationHelper.TeamAdminPolicy)).Succeeded.Should().BeFalse();
        (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Team_admin_policy_observes_demotion_and_user_deactivation_without_a_new_login()
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { IamId = "sandbox-10001", Name = "Member" };
        var permission = new TeamPermission
        {
            User = user, Team = new Team { Name = "Team A", Slug = "team-a" }, Role = TeamRole.Admin,
        };
        db.TeamPermissions.Add(permission);
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();

        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().BeTrue();
        permission.Role = TeamRole.Editor;
        await db.SaveChangesAsync();
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().BeFalse();
        user.IsAdmin = true;
        await db.SaveChangesAsync();
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().BeTrue();
        user.IsActive = false;
        await db.SaveChangesAsync();
        (await AuthorizeTeam(_scope.ServiceProvider, principal, "team-a", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Admin_access_endpoint_returns_no_content_and_disables_response_caching()
    {
        var cache = typeof(AdminController).GetCustomAttribute<ResponseCacheAttribute>()!;

        cache.NoStore.Should().BeTrue();
        cache.Location.Should().Be(ResponseCacheLocation.None);
        using var db = TestDbContextFactory.CreateInMemory();
        new AdminController(db, new FakeRosettaService()).Access().Should().BeOfType<NoContentResult>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_login_creates_the_user_before_issuing_a_cookie(bool local)
    {
        using var provider = CreateProvider(local);
        using var scope = provider.CreateScope();
        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("preferred_username", "sample@example.test"));
        var startedAt = DateTimeOffset.UtcNow;

        var context = await SignIn(scope.ServiceProvider, principal, local);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.IamId.Should().Be("sandbox-10001");
        user.Name.Should().Be("Sample User");
        user.Email.Should().Be(local ? "sample@example.test" : null);
        user.IsAdmin.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.CreatedAt.Should().BeOnOrAfter(startedAt).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        user.UpdatedAt.Should().Be(user.CreatedAt);
        user.LastLoginAt.Should().Be(user.CreatedAt);
        context.Response.Headers.SetCookie.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("changed@health.ucdavis.edu")]
    public async Task First_Entra_login_uses_Rosetta_profile_and_later_login_preserves_campus_email_without_directory_calls(
        string? laterLoginEmail)
    {
        var rosetta = new FakeRosettaService();
        rosetta.People.Add(new Server.Models.Directory.DirectoryPerson
        {
            IamId = "sandbox-10001", Name = "Directory Name", Email = "directory@ucdavis.edu", Kerberos = "kerb1",
        });
        using var provider = CreateProvider(rosetta: rosetta);
        using var scope = provider.CreateScope();

        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("preferred_username", "directory@health.ucdavis.edu"));
        await SignIn(scope.ServiceProvider, principal);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Directory Name");
        user.Email.Should().Be("directory@ucdavis.edu");
        rosetta.LookupCalls.Should().Equal("sandbox-10001");
        db.Model.FindEntityType(typeof(User))!.FindProperty("Kerberos").Should().BeNull();

        rosetta.Failure = new HttpRequestException("Directory unavailable");
        var laterPrincipal = CreatePrincipal();
        if (laterLoginEmail != null)
        {
            ((ClaimsIdentity)laterPrincipal.Identity!).AddClaim(new Claim("preferred_username", laterLoginEmail));
        }
        await SignIn(scope.ServiceProvider, laterPrincipal);

        rosetta.LookupCalls.Should().ContainSingle();
        (await db.Users.SingleAsync()).Email.Should().Be("directory@ucdavis.edu");
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entra_login_without_a_campus_email_never_saves_the_health_login_address(bool foundInRosetta)
    {
        var rosetta = new FakeRosettaService();
        if (foundInRosetta)
        {
            rosetta.People.Add(new Server.Models.Directory.DirectoryPerson
            {
                IamId = "sandbox-10001", Name = "Directory Name", Kerberos = "kerb1", IsActiveInIam = true,
            });
        }
        using var provider = CreateProvider(rosetta: rosetta);
        using var scope = provider.CreateScope();
        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("preferred_username", "person@health.ucdavis.edu"));

        await SignIn(scope.ServiceProvider, principal);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        user.Email.Should().BeNull();
        user.Name.Should().Be(foundInRosetta ? "Directory Name" : "Sample User");
    }

    [Fact]
    public async Task First_login_directory_failure_does_not_save_a_user_or_issue_a_cookie()
    {
        var rosetta = new FakeRosettaService { Failure = new HttpRequestException("Directory unavailable") };
        using var provider = CreateProvider(rosetta: rosetta);
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var signIn = () => context.SignInAsync(GetCookieScheme(false), CreatePrincipal());

        await signIn.Should().ThrowAsync<HttpRequestException>();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync()).Should().BeFalse();
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("basic")]
    public async Task Local_sandbox_login_does_not_call_Rosetta(string persona)
    {
        var rosetta = new FakeRosettaService { Failure = new HttpRequestException("Directory unavailable") };
        using var provider = CreateProvider(local: true, rosetta: rosetta);
        using var scope = provider.CreateScope();

        await SignIn(scope.ServiceProvider, LocalAuthentication.CreatePrincipal(persona)!, local: true);

        rosetta.LookupCalls.Should().BeEmpty();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false, "sandbox-10001", true)]
    [InlineData(true, "sandbox-10001", true)]
    [InlineData(false, "other-id, , sandbox-10001 , sandbox-10001,,", true)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(true, " , , ", false)]
    [InlineData(false, "sample-user", false)]
    [InlineData(false, "sandbox-1000,sandbox-100010,other-id", false)]
    public async Task Development_login_grants_admin_only_for_a_configured_exact_IAM_ID(
        bool local, string? adminIamIds, bool expectedAdmin)
    {
        using var provider = CreateProvider(local, adminIamIds: adminIamIds, environmentName: Environments.Development);
        using var scope = provider.CreateScope();

        var context = await SignIn(scope.ServiceProvider, CreatePrincipal(), local);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.IsAdmin.Should().Be(expectedAdmin);
        user.IsActive.Should().BeTrue();
        context.Response.Headers.SetCookie.Should().NotBeEmpty();
        (await AuthorizeSiteAdmin(scope.ServiceProvider, CreatePrincipal())).Succeeded.Should().Be(expectedAdmin);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Site_admin_policy_requires_an_active_global_admin_regardless_of_team_or_claim_roles(
        bool isAdmin, bool isActive, bool expectedAccess)
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new Server.Core.Domain.User
        {
            IamId = "sandbox-10001",
            Name = "Team Admin",
            IsAdmin = isAdmin,
            IsActive = isActive,
        };
        db.TeamPermissions.Add(new Server.Core.Domain.TeamPermission
        {
            User = user,
            Team = new Server.Core.Domain.Team { Name = "Test Team", Slug = "test-team" },
            Role = TeamRole.Admin,
        });
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "SiteAdmin"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "SampleRole"));

        var result = await AuthorizeSiteAdmin(_scope.ServiceProvider, principal);

        result.Succeeded.Should().Be(expectedAccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("another-iam-id")]
    [InlineData("sample-user")]
    public async Task Site_admin_policy_denies_missing_or_nonmatching_IAM_IDs(string? iamId)
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Server.Core.Domain.User
        {
            IamId = "sandbox-10001",
            Name = "Site Admin",
            IsAdmin = true,
        });
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst("ucdPersonIAMID")!);
        if (iamId != null)
        {
            identity.AddClaim(new Claim("ucdPersonIAMID", iamId));
        }

        var result = await AuthorizeSiteAdmin(_scope.ServiceProvider, principal);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Site_admin_policy_denies_users_without_a_database_record()
    {
        var result = await AuthorizeSiteAdmin(_scope.ServiceProvider, CreatePrincipal());

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Site_admin_policy_denies_unauthenticated_principals_with_matching_admin_claims()
    {
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Server.Core.Domain.User
        {
            IamId = "sandbox-10001",
            Name = "Site Admin",
            IsAdmin = true,
        });
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(CreatePrincipal().Claims));

        var result = await AuthorizeSiteAdmin(_scope.ServiceProvider, principal);

        result.Succeeded.Should().BeFalse();
        (await _scope.ServiceProvider.GetRequiredService<IUserService>().IsSiteAdmin(principal)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Site_admin_policy_denies_without_an_HTTP_resource(bool useNullResource)
    {
        var authorization = _scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(CreatePrincipal(),
            useNullResource ? null : new object(), AuthenticationHelper.SiteAdminPolicy);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Site_admin_policy_reads_revoked_access_from_the_database_without_a_new_login()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SiteAdminRevocation_{Guid.NewGuid():N}", new InMemoryDatabaseRoot()).Options;
        using var provider = CreateProvider(createDbContext: () => new AppDbContext(options));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new Server.Core.Domain.User
        {
            IamId = "sandbox-10001",
            Name = "Site Admin",
            IsAdmin = true,
        });
        await db.SaveChangesAsync();
        var principal = CreatePrincipal();
        (await AuthorizeSiteAdmin(scope.ServiceProvider, principal)).Succeeded.Should().BeTrue();

        using (var updateScope = provider.CreateScope())
        {
            var updateDb = updateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await updateDb.Users.SingleAsync()).IsAdmin = false;
            await updateDb.SaveChangesAsync();
        }

        var result = await AuthorizeSiteAdmin(scope.ServiceProvider, principal);

        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Login_ignores_development_admin_configuration_outside_Development(string environmentName)
    {
        using var provider = CreateProvider(adminIamIds: "sandbox-10001", environmentName: environmentName);
        using var scope = provider.CreateScope();

        await SignIn(scope.ServiceProvider, CreatePrincipal());

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync()).IsAdmin.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Development_login_promotes_an_existing_user_without_reactivating_the_account(bool local)
    {
        using var provider = CreateProvider(local, adminIamIds: "sandbox-10001", environmentName: Environments.Development);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var user = new Server.Core.Domain.User
        {
            IamId = "sandbox-10001",
            Name = "Previous Name",
            CreatedAt = createdAt,
            IsAdmin = false,
            IsActive = false,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var userId = user.Id;
        db.ChangeTracker.Clear();

        await SignIn(scope.ServiceProvider, CreatePrincipal(), local);

        db.ChangeTracker.Clear();
        user = await db.Users.SingleAsync();
        user.Id.Should().Be(userId);
        user.CreatedAt.Should().Be(createdAt);
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other-id")]
    public async Task Removing_a_development_admin_from_configuration_does_not_demote_the_user(string? adminIamIds)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DevelopmentAdmin_{Guid.NewGuid():N}", new InMemoryDatabaseRoot()).Options;
        using (var provider = CreateProvider(createDbContext: () => new AppDbContext(options),
            adminIamIds: "sandbox-10001", environmentName: Environments.Development))
        using (var scope = provider.CreateScope())
        {
            await SignIn(scope.ServiceProvider, CreatePrincipal());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync()).IsAdmin.Should().BeTrue();
        }

        using var laterProvider = CreateProvider(createDbContext: () => new AppDbContext(options),
            adminIamIds: adminIamIds, environmentName: Environments.Development);
        using var laterScope = laterProvider.CreateScope();

        await SignIn(laterScope.ServiceProvider, CreatePrincipal());

        var laterDb = laterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        laterDb.ChangeTracker.Clear();
        (await laterDb.Users.SingleAsync()).IsAdmin.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Later_login_updates_the_same_IAM_user_and_preserves_application_fields(bool local)
    {
        using var provider = CreateProvider(local);
        using var scope = provider.CreateScope();
        await SignIn(scope.ServiceProvider, CreatePrincipal(), local);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        var userId = user.Id;
        var originalCreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        user.CreatedAt = originalCreatedAt;
        user.UpdatedAt = originalCreatedAt;
        user.LastLoginAt = originalCreatedAt;
        user.Email = "saved@ucdavis.edu";
        user.IsAdmin = true;
        user.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var principal = CreatePrincipal("changed-directory-subject");
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst("name")!);
        identity.AddClaim(new Claim("name", "Updated User"));
        identity.AddClaim(new Claim("preferred_username", "updated@example.test"));
        var startedAt = DateTimeOffset.UtcNow;

        await SignIn(scope.ServiceProvider, principal, local);

        db.ChangeTracker.Clear();
        user = await db.Users.SingleAsync();
        user.Id.Should().Be(userId);
        user.IamId.Should().Be("sandbox-10001");
        user.Name.Should().Be("Updated User");
        user.Email.Should().Be(local ? "updated@example.test" : "saved@ucdavis.edu");
        user.CreatedAt.Should().Be(originalCreatedAt);
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().BeOnOrAfter(startedAt).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        user.LastLoginAt.Should().Be(user.UpdatedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Concurrent_first_login_refreshes_the_competing_user_without_losing_application_fields(
        bool local, bool configureAdmin)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"LoginCollision_{Guid.NewGuid():N}", new InMemoryDatabaseRoot()).Options;
        var createdAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var competingUser = new Server.Core.Domain.User
        {
            Id = 123,
            IamId = "sandbox-10001",
            Name = "Previous Name",
            Email = "previous@ucdavis.edu",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            LastLoginAt = createdAt,
            IsAdmin = !configureAdmin,
            IsActive = false,
        };
        using var provider = CreateProvider(local, () => new FirstSaveFailureDbContext(options, async () =>
        {
            await using var competingDb = new AppDbContext(options);
            createdAt = DateTimeOffset.UtcNow;
            competingUser.CreatedAt = createdAt;
            competingDb.Users.Add(competingUser);
            await competingDb.SaveChangesAsync();
        }), adminIamIds: configureAdmin ? "sandbox-10001" : null, environmentName: Environments.Development);
        using var scope = provider.CreateScope();
        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("preferred_username", "current@example.test"));
        var startedAt = DateTimeOffset.UtcNow;

        var context = await SignIn(scope.ServiceProvider, principal, local);

        var db = (FirstSaveFailureDbContext)scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.Id.Should().Be(competingUser.Id);
        user.Name.Should().Be("Sample User");
        user.Email.Should().Be(local ? "current@example.test" : "previous@ucdavis.edu");
        user.CreatedAt.Should().Be(createdAt);
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().BeOnOrAfter(startedAt).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        user.LastLoginAt.Should().Be(user.UpdatedAt);
        db.SaveAttempts.Should().Be(2);
        user.LastLoginAt.Should().BeOnOrAfter(user.CreatedAt);
        context.Response.Headers.SetCookie.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persistence_failure_without_a_competing_user_does_not_issue_a_cookie_or_retry(bool local)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"LoginFailure_{Guid.NewGuid():N}").Options;
        using var provider = CreateProvider(local, () => new FirstSaveFailureDbContext(options));
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var signIn = () => context.SignInAsync(GetCookieScheme(local), CreatePrincipal());

        await signIn.Should().ThrowAsync<DbUpdateException>().WithMessage("Simulated login persistence failure.");

        var db = (FirstSaveFailureDbContext)scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync()).Should().BeFalse();
        db.SaveAttempts.Should().Be(1);
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "ucdPersonIAMID", null)]
    [InlineData(false, "ucdPersonIAMID", "")]
    [InlineData(false, "ucdPersonIAMID", " ")]
    [InlineData(false, "name", null)]
    [InlineData(false, "name", "")]
    [InlineData(false, "name", " ")]
    [InlineData(true, "ucdPersonIAMID", null)]
    [InlineData(true, "ucdPersonIAMID", "")]
    [InlineData(true, "ucdPersonIAMID", " ")]
    [InlineData(true, "name", null)]
    [InlineData(true, "name", "")]
    [InlineData(true, "name", " ")]
    public async Task Login_with_missing_required_profile_claims_does_not_create_a_user_or_cookie(
        bool local, string claimType, string? claimValue)
    {
        using var provider = CreateProvider(local);
        using var scope = provider.CreateScope();
        var principal = CreatePrincipal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(claimType)!);
        if (claimValue != null)
        {
            identity.AddClaim(new Claim(claimType, claimValue));
        }
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var signIn = () => context.SignInAsync(GetCookieScheme(local), principal);

        await signIn.Should().ThrowAsync<InvalidOperationException>();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync()).Should().BeFalse();
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unauthenticated_principal_does_not_create_a_user_or_cookie(bool local)
    {
        using var provider = CreateProvider(local);
        using var scope = provider.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(CreatePrincipal().Claims));
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var signIn = () => context.SignInAsync(GetCookieScheme(local), principal);

        await signIn.Should().ThrowAsync<InvalidOperationException>();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync()).Should().BeFalse();
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, ClaimTypes.Email)]
    [InlineData(false, "email")]
    [InlineData(true, ClaimTypes.Email)]
    [InlineData(true, "email")]
    public async Task Login_accepts_the_identity_name_and_only_saves_alternate_email_claims_in_local_mode(bool local, string emailClaimType)
    {
        using var provider = CreateProvider(local);
        using var scope = provider.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("ucdPersonIAMID", "sandbox-10001"), new Claim(ClaimTypes.Name, "Alternate User"),
                new Claim(emailClaimType, "alternate@example.test")], "TestAuth"));

        await SignIn(scope.ServiceProvider, principal, local);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Alternate User");
        user.Email.Should().Be(local ? "alternate@example.test" : null);
    }

    [Theory]
    [InlineData("basic", false)]
    [InlineData("sample", true)]
    public async Task Local_login_preserves_the_persona_role_boundary(string persona, bool hasSampleRole)
    {
        using var provider = CreateProvider(local: true);
        using var scope = provider.CreateScope();
        var principal = LocalAuthentication.CreatePrincipal(persona)!;

        var context = await SignIn(scope.ServiceProvider, principal, local: true);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(LocalAuthentication.Scheme);
        var cookieValue = context.Response.Headers.SetCookie
            .Single(cookie => cookie!.StartsWith(options.Cookie.Name + "=", StringComparison.Ordinal))!
            .Split(';')[0].Split('=', 2)[1];
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookieValue));

        ticket.Should().NotBeNull();
        ticket!.Principal.IsInRole("User").Should().BeTrue();
        ticket.Principal.IsInRole("SampleRole").Should().Be(hasSampleRole);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task People_login_saves_directory_profile_and_preserves_existing_application_permissions(bool existingUser, bool isAdmin)
    {
        using var provider = CreateProvider(local: true);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var person = new Person
        {
            IamId = "10010001", FullName = "Jordan Demo", Email = "jordan@example.test", UserId = "jdemo", IsActiveInIam = true,
        };
        db.People.Add(person);
        var createdAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var previousUser = new User
        {
            IamId = person.IamId, Name = "Previous Name", Email = "previous@example.test", IsAdmin = isAdmin,
            CreatedAt = createdAt, UpdatedAt = createdAt, LastLoginAt = createdAt,
        };
        if (existingUser)
        {
            db.TeamPermissions.Add(new TeamPermission
            {
                User = previousUser, Team = new Team { Name = "Demo", Slug = "demo" }, Role = TeamRole.Admin,
            });
        }
        await db.SaveChangesAsync();
        var controller = new AccountController(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }, RouteData = new RouteData(),
            },
        };
        controller.Url = new UrlHelper(controller.ControllerContext);
        var startedAt = DateTimeOffset.UtcNow;

        var result = await controller.LocalPersonLogin(person.Email, "/teams/demo", db);

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/teams/demo");
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.IamId.Should().Be(person.IamId);
        user.Name.Should().Be(person.FullName);
        user.Email.Should().Be(person.Email);
        user.IsAdmin.Should().Be(isAdmin);
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().Be(user.UpdatedAt);
        user.UpdatedAt.Should().BeOnOrAfter(startedAt).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        if (existingUser)
        {
            user.Id.Should().Be(previousUser.Id);
            user.CreatedAt.Should().Be(createdAt);
            (await db.TeamPermissions.SingleAsync()).Role.Should().Be(TeamRole.Admin);
        }
        else
        {
            user.CreatedAt.Should().BeOnOrAfter(startedAt);
            (await db.TeamPermissions.AnyAsync()).Should().BeFalse();
        }

        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(LocalAuthentication.Scheme);
        var cookieValue = controller.Response.Headers.SetCookie
            .Single(cookie => cookie!.StartsWith(options.Cookie.Name + "=", StringComparison.Ordinal))!
            .Split(';')[0].Split('=', 2)[1];
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookieValue));
        ticket.Should().NotBeNull();
        ticket!.Principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("local-person:10010001");
        ticket.Principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Should().Equal("User");
        (await AuthorizeSiteAdmin(services, ticket.Principal)).Succeeded.Should().Be(isAdmin);
        (await AuthorizeTeam(services, ticket.Principal, "demo", AuthenticationHelper.TeamAdminPolicy))
            .Succeeded.Should().Be(existingUser);
    }

    [Fact]
    public async Task Token_and_cookie_validation_do_not_create_users_or_refresh_login_timestamps()
    {
        var principal = CreatePrincipal();
        var db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var signedIn = await ValidateToken(principal);
        await ValidateCookie(signedIn);

        (await db.Users.AnyAsync()).Should().BeFalse();

        await SignIn(_scope.ServiceProvider, signedIn);
        var user = await db.Users.SingleAsync();
        var previousLogin = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        user.UpdatedAt = previousLogin;
        user.LastLoginAt = previousLogin;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        signedIn = await ValidateToken(principal);
        await ValidateCookie(signedIn);

        db.ChangeTracker.Clear();
        user = await db.Users.SingleAsync();
        user.UpdatedAt.Should().Be(previousLogin);
        user.LastLoginAt.Should().Be(previousLogin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sign_in_and_cookie_validation_use_only_application_roles(bool hasDirectoryRoles)
    {
        var principal = CreatePrincipal();
        if (hasDirectoryRoles)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "DirectoryRole"));
        }

        var signedIn = await ValidateToken(principal);
        var cookie = await ValidateCookie(signedIn);

        signedIn.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().BeEquivalentTo("User", "SampleRole");
        signedIn.Identity!.Name.Should().Be("Sample User");
        cookie.Principal.Should().BeSameAs(signedIn);
        cookie.ShouldRenew.Should().BeFalse();
    }

    [Theory]
    [InlineData("User", "RemovedRole")]
    [InlineData("User", "User")]
    public async Task Cookie_validation_replaces_stale_or_duplicate_roles(string firstRole, string secondRole)
    {
        var principal = CreatePrincipal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, firstRole));
        identity.AddClaim(new Claim(ClaimTypes.Role, secondRole));

        var cookie = await ValidateCookie(principal);

        cookie.Principal!.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().BeEquivalentTo("User", "SampleRole");
        cookie.Principal.Identity!.Name.Should().Be("Sample User");
        cookie.Principal.FindFirst("ucdPersonIAMID")!.Value.Should().Be("sandbox-10001");
        cookie.ShouldRenew.Should().BeTrue();
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().Equal(firstRole, secondRole);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Sign_in_and_cookie_validation_leave_a_missing_user_id_unchanged(string? userId)
    {
        var principal = CreatePrincipal(userId);

        var signedIn = await ValidateToken(principal);
        var cookie = await ValidateCookie(signedIn);

        signedIn.Should().BeSameAs(principal);
        cookie.Principal.Should().BeSameAs(principal);
        cookie.ShouldRenew.Should().BeFalse();
        principal.FindAll(ClaimTypes.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task Role_updates_preserve_identities_and_non_role_claims_without_mutating_the_input()
    {
        var principal = CreatePrincipal();
        var primary = (ClaimsIdentity)principal.Identity!;
        primary.Label = "Entra identity";
        primary.AddClaim(new Claim(ClaimTypes.Role, "DirectoryRole"));
        var secondary = new ClaimsIdentity(
            [new Claim("department", "Example department"), new Claim(ClaimTypes.Role, "OldRole")],
            "AdditionalIdentity");
        principal.AddIdentity(secondary);

        var signedIn = await ValidateToken(principal);

        signedIn.Identities.Should().HaveCount(2);
        var updatedPrimary = signedIn.Identities.First();
        updatedPrimary.AuthenticationType.Should().Be(primary.AuthenticationType);
        updatedPrimary.NameClaimType.Should().Be(primary.NameClaimType);
        updatedPrimary.RoleClaimType.Should().Be(primary.RoleClaimType);
        updatedPrimary.Label.Should().Be(primary.Label);
        updatedPrimary.Name.Should().Be("Sample User");
        updatedPrimary.FindFirst("ucdPersonIAMID")!.Value.Should().Be("sandbox-10001");
        var updatedSecondary = signedIn.Identities.Last();
        updatedSecondary.AuthenticationType.Should().Be("AdditionalIdentity");
        updatedSecondary.FindFirst("department")!.Value.Should().Be("Example department");
        updatedSecondary.FindAll(ClaimTypes.Role).Should().BeEmpty();
        signedIn.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().BeEquivalentTo("User", "SampleRole");
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().Equal("DirectoryRole", "OldRole");
    }

    [Theory]
    [InlineData(false, ClaimTypes.Role)]
    [InlineData(true, ClaimTypes.Role)]
    [InlineData(false, "application-role")]
    [InlineData(true, "application-role")]
    public async Task Role_updates_honor_each_identity_role_claim_type(bool validateCookie, string primaryRoleClaimType)
    {
        var primary = new ClaimsIdentity(CreatePrincipal().Claims, "OpenIdConnect", "name", primaryRoleClaimType);
        primary.AddClaim(new Claim(primaryRoleClaimType, "User"));
        primary.AddClaim(new Claim(primaryRoleClaimType, "SampleRole"));
        var secondary = new ClaimsIdentity(
            [new Claim("secondary-role", "StaleRole"), new Claim("department", "Example department")],
            "AdditionalIdentity", "name", "secondary-role");
        var principal = new ClaimsPrincipal([primary, secondary]);
        principal.IsInRole("StaleRole").Should().BeTrue();

        ClaimsPrincipal updated;
        if (validateCookie)
        {
            var cookie = await ValidateCookie(principal);
            cookie.ShouldRenew.Should().BeTrue();
            updated = cookie.Principal!;
        }
        else
        {
            updated = await ValidateToken(principal);
        }

        updated.IsInRole("StaleRole").Should().BeFalse();
        updated.IsInRole("User").Should().BeTrue();
        updated.IsInRole("SampleRole").Should().BeTrue();
        updated.Identities.First().RoleClaimType.Should().Be(primaryRoleClaimType);
        updated.Identities.Last().RoleClaimType.Should().Be("secondary-role");
        updated.FindFirst("department")!.Value.Should().Be("Example department");
        principal.IsInRole("StaleRole").Should().BeTrue();
    }

    private static ClaimsPrincipal CreatePrincipal(string? userId = "sample-user")
    {
        var claims = new List<Claim>
        {
            new("name", "Sample User"),
            new("ucdPersonIAMID", "sandbox-10001"),
        };
        if (userId != null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "OpenIdConnect", "name", ClaimTypes.Role));
    }

    private static string GetCookieScheme(bool local)
        => local ? LocalAuthentication.Scheme : CookieAuthenticationDefaults.AuthenticationScheme;

    private static Task<AuthorizationResult> AuthorizeSiteAdmin(IServiceProvider services, ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { RequestServices = services, User = principal };
        return services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, context, AuthenticationHelper.SiteAdminPolicy);
    }

    private static Task<AuthorizationResult> AuthorizeTeam(IServiceProvider services, ClaimsPrincipal principal, string? slug,
        string policy = AuthenticationHelper.TeamAccessPolicy)
    {
        var context = new DefaultHttpContext { RequestServices = services, User = principal };
        if (slug != null)
        {
            context.Request.RouteValues["teamSlug"] = slug;
        }

        return services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, context, policy);
    }

    private static async Task<DefaultHttpContext> SignIn(
        IServiceProvider services, ClaimsPrincipal principal, bool local = false)
    {
        if (local)
        {
            principal = new ClaimsPrincipal(principal.Identities.Select(identity =>
                new ClaimsIdentity(identity.Claims, LocalAuthentication.Scheme, identity.NameClaimType, identity.RoleClaimType)));
        }
        var context = new DefaultHttpContext { RequestServices = services };
        await context.SignInAsync(GetCookieScheme(local), principal);
        return context;
    }

    private async Task<ClaimsPrincipal> ValidateToken(ClaimsPrincipal principal)
    {
        var options = _scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler));
        var context = new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = _scope.ServiceProvider }, scheme, options, principal, new AuthenticationProperties());

        await options.Events.TokenValidated(context);

        return context.Principal!;
    }

    private async Task<CookieValidatePrincipalContext> ValidateCookie(ClaimsPrincipal principal)
    {
        var options = _scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), scheme.Name);
        var context = new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = _scope.ServiceProvider }, scheme, options, ticket);

        await options.Events.ValidatePrincipal(context);

        return context;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private sealed class FirstSaveFailureDbContext(
        DbContextOptions<AppDbContext> options, Func<Task>? beforeFailure = null) : AppDbContext(options)
    {
        public int SaveAttempts { get; private set; }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (SaveAttempts == 1)
            {
                if (beforeFailure != null)
                {
                    await beforeFailure();
                }
                throw new DbUpdateException("Simulated login persistence failure.");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

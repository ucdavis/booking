using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Server.Core.Data;
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

    private static ServiceProvider CreateProvider(bool local = false, Func<AppDbContext>? createDbContext = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Instance"] = "https://login.microsoftonline.com/",
            ["Auth:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["Auth:UseLocal"] = local.ToString(),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped(_ => createDbContext?.Invoke() ?? TestDbContextFactory.CreateInMemory());
        services.AddScoped<IUserService, UserService>();
        services.AddAuthenticationServices(configuration, new TestEnvironment
        {
            EnvironmentName = local ? Environments.Development : Environments.Production,
        });
        return services.BuildServiceProvider();
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
        user.Email.Should().Be("sample@example.test");
        user.IsAdmin.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.CreatedAt.Should().BeOnOrAfter(startedAt).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        user.UpdatedAt.Should().Be(user.CreatedAt);
        user.LastLoginAt.Should().Be(user.CreatedAt);
        context.Response.Headers.SetCookie.Should().NotBeEmpty();
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
        user.Email.Should().Be("updated@example.test");
        user.CreatedAt.Should().Be(originalCreatedAt);
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.UpdatedAt.Should().BeOnOrAfter(startedAt).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        user.LastLoginAt.Should().Be(user.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_first_login_refreshes_the_competing_user_without_losing_application_fields(bool local)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"LoginCollision_{Guid.NewGuid():N}", new InMemoryDatabaseRoot()).Options;
        var createdAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var competingUser = new Server.Core.Domain.User
        {
            Id = 123,
            IamId = "sandbox-10001",
            Name = "Previous Name",
            Email = "previous@example.test",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            LastLoginAt = createdAt,
            IsAdmin = true,
            IsActive = false,
        };
        using var provider = CreateProvider(local, () => new FirstSaveFailureDbContext(options, async () =>
        {
            await using var competingDb = new AppDbContext(options);
            createdAt = DateTimeOffset.UtcNow;
            competingUser.CreatedAt = createdAt;
            competingDb.Users.Add(competingUser);
            await competingDb.SaveChangesAsync();
        }));
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
        user.Email.Should().Be("current@example.test");
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
    public async Task Login_accepts_the_identity_name_and_alternate_email_claims(bool local, string emailClaimType)
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
        user.Email.Should().Be("alternate@example.test");
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
        var cookieValue = context.Response.Headers.SetCookie.Single()!.Split(';')[0].Split('=', 2)[1];
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookieValue));

        ticket.Should().NotBeNull();
        ticket!.Principal.IsInRole("User").Should().BeTrue();
        ticket.Principal.IsInRole("SampleRole").Should().Be(hasSampleRole);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.CountAsync()).Should().Be(1);
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

    private static async Task<DefaultHttpContext> SignIn(
        IServiceProvider services, ClaimsPrincipal principal, bool local = false)
    {
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

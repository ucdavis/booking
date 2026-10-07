using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Middleware;
using Server.Services;

namespace Server.Tests.Middleware;

public class EmulationMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authenticated_pipeline_uses_target_identity_and_preserves_the_real_actor(bool local)
    {
        using var fixture = new Fixture(local);
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();
        var selection = fixture.Select(login.SessionId);

        var result = await fixture.RequestAsync(login.Cookie, selection, async context =>
        {
            var users = context.RequestServices.GetRequiredService<IUserService>();
            context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.TargetIamId);
            context.User.FindFirst("name")!.Value.Should().Be("Target User");
            context.User.FindFirst("preferred_username")!.Value.Should().Be("target@example.test");
            context.User.IsInRole("ActorOnlyRole").Should().BeFalse();
            context.User.HasClaim("actor-only", "private").Should().BeFalse();
            EmulationService.IsEmulating(context).Should().BeTrue();
            (await users.IsSiteAdmin(context.User)).Should().BeFalse();
            var actor = EmulationService.GetActor(context);
            actor.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
            actor.HasClaim("actor-only", "private").Should().BeTrue();
            (await users.IsSiteAdmin(actor)).Should().BeTrue();
        });

        result.ReachedEndpoint.Should().BeTrue();
        result.Context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData(AuthenticationHelper.SiteAdminPolicy, null, false)]
    [InlineData(AuthenticationHelper.TeamAccessPolicy, "target-team", true)]
    [InlineData(AuthenticationHelper.TeamAdminPolicy, "target-team", false)]
    [InlineData(AuthenticationHelper.TeamAccessPolicy, "actor-team", false)]
    public async Task Authorization_middleware_checks_target_permissions_without_restoring_actor(
        string policy, string? teamSlug, bool allowed)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();

        var result = await fixture.RequestAsync(login.Cookie, fixture.Select(login.SessionId),
            policy: policy, teamSlug: teamSlug);

        result.ReachedEndpoint.Should().Be(allowed);
        result.Context.Response.StatusCode.Should().Be(allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden);
        result.Context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.TargetIamId);
        EmulationService.GetActor(result.Context).FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
    }

    [Fact]
    public async Task No_selection_keeps_the_authenticated_user_and_permissions()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();

        var result = await fixture.RequestAsync(login.Cookie, policy: AuthenticationHelper.SiteAdminPolicy);

        result.ReachedEndpoint.Should().BeTrue();
        result.Context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
        EmulationService.IsEmulating(result.Context).Should().BeFalse();
        EmulationService.GetActor(result.Context).Should().BeSameAs(result.Context.User);
    }

    [Fact]
    public async Task Anonymous_requests_clear_selection_without_creating_an_identity()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        var result = await fixture.RequestAsync(null, fixture.Select("previous-session"), requireAuthentication: false);

        result.ReachedEndpoint.Should().BeTrue();
        (result.Context.User.Identity?.IsAuthenticated).Should().NotBe(true);
        AssertSelectionCleared(result.Context);
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("different-actor")]
    [InlineData("different-session")]
    [InlineData("expired")]
    public async Task Invalid_selection_blocks_requests_instead_of_restoring_admin_permissions(string invalidKind)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();
        var selection = invalidKind switch
        {
            "forged" => Fixture.SelectionCookieName + "=invalid-ticket",
            "different-actor" => fixture.Select(login.SessionId, Fixture.Actor("different-admin")),
            "different-session" => fixture.Select("another-login-session"),
            "expired" => fixture.ExpireSelection(fixture.Select(login.SessionId)),
            _ => throw new InvalidOperationException(),
        };

        var result = await fixture.RequestAsync(login.Cookie, selection);

        AssertBlocked(result);
        result.Context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        result.Context.Response.Headers.SetCookie.Should().NotContain(value => value!.StartsWith(Fixture.SelectionCookieName + "="));
    }

    [Theory]
    [InlineData("inactive-target")]
    [InlineData("missing-target")]
    [InlineData("revoked-admin")]
    [InlineData("inactive-admin")]
    public async Task Revocation_and_unavailable_targets_block_the_existing_selection(string change)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();
        var selection = fixture.Select(login.SessionId);
        await fixture.ChangeUsersAsync(async db =>
        {
            var actor = await db.Users.SingleAsync(user => user.IamId == Fixture.ActorIamId);
            var target = await db.Users.SingleAsync(user => user.IamId == Fixture.TargetIamId);
            switch (change)
            {
                case "inactive-target": target.IsActive = false; break;
                case "missing-target": db.Users.Remove(target); break;
                case "revoked-admin": actor.IsAdmin = false; break;
                case "inactive-admin": actor.IsActive = false; break;
            }
        });

        AssertBlocked(await fixture.RequestAsync(login.Cookie, selection));
    }

    [Theory]
    [InlineData("/api/user/me")]
    [InlineData("/api/antiforgery")]
    [InlineData("/api/emulation/stop")]
    public async Task Directory_failure_does_not_interrupt_an_active_emulation_selection(string path)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();
        var selection = fixture.Select(login.SessionId);
        fixture.Rosetta.Failure = new HttpRequestException("Directory unavailable");

        var result = await fixture.RequestAsync(login.Cookie, selection, path: path);

        result.ReachedEndpoint.Should().BeTrue();
        EmulationService.IsEmulating(result.Context).Should().BeTrue();
        result.Context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.TargetIamId);
        result.Context.User.IsInRole("ActorOnlyRole").Should().BeFalse();
        fixture.Rosetta.LookupCalls.Should().BeEmpty();
        EmulationService.GetActor(result.Context).FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
    }

    [Theory]
    [InlineData("/api/user/me")]
    [InlineData("/api/antiforgery")]
    [InlineData("/api/emulation/stop")]
    [InlineData("/logout/antiforgery")]
    [InlineData("/logout")]
    public async Task Invalid_selection_allows_explicit_recovery_with_an_unprivileged_emulation_identity(string path)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();

        var result = await fixture.RequestAsync(login.Cookie, Fixture.SelectionCookieName + "=invalid-ticket",
            async context =>
            {
                var controller = new UserController(
                    context.RequestServices.GetRequiredService<IUserService>(),
                    context.RequestServices.GetRequiredService<AppDbContext>())
                {
                    ControllerContext = new ControllerContext { HttpContext = context },
                };
                var response = (await controller.Me()).Should().BeOfType<OkObjectResult>().Subject;
                var profile = JsonSerializer.SerializeToElement(response.Value);
                profile.GetProperty("IsEmulating").GetBoolean().Should().BeTrue();
                profile.GetProperty("IsSiteAdmin").GetBoolean().Should().BeFalse();
                profile.GetProperty("Name").GetString().Should().Be("Emulation unavailable");
                profile.GetProperty("Kerberos").ValueKind.Should().Be(JsonValueKind.Null);
            }, path: path);

        result.ReachedEndpoint.Should().BeTrue();
        result.Context.User.FindFirst("ucdPersonIAMID").Should().BeNull();
        result.Context.User.FindAll(ClaimTypes.Role).Should().BeEmpty();
        EmulationService.GetActor(result.Context).FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
    }

    [Theory]
    [InlineData("/temp", "GET", true, true)]
    [InlineData("/api/example", "GET", true, false)]
    [InlineData("/temp", "POST", true, false)]
    [InlineData("/temp", "GET", false, false)]
    public async Task Invalid_selection_can_load_the_public_app_shell_but_cannot_bypass_protected_requests(
        string path, string method, bool allowAnonymous, bool allowed)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();

        var result = await fixture.RequestAsync(login.Cookie, Fixture.SelectionCookieName + "=invalid-ticket",
            path: path, allowAnonymous: allowAnonymous, method: method);

        result.ReachedEndpoint.Should().Be(allowed);
        result.Context.Response.StatusCode.Should().Be(allowed ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden);
        EmulationService.IsEmulating(result.Context).Should().BeTrue();
        result.Context.User.FindFirst("ucdPersonIAMID").Should().BeNull();
        result.Context.User.FindAll(ClaimTypes.Role).Should().BeEmpty();
        EmulationService.GetActor(result.Context).FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
    }

    [Theory]
    [InlineData("/api/emulation/stop")]
    [InlineData("/logout")]
    public async Task Shared_antiforgery_endpoint_refreshes_tokens_for_recovery_after_the_target_becomes_unavailable(string recoveryPath)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();
        var selection = fixture.Select(login.SessionId);
        AntiforgeryTokenSet? originalTokens = null;
        var originalResponse = await fixture.RequestAsync(login.Cookie, selection, context =>
        {
            originalTokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            return Task.CompletedTask;
        }, path: "/api/antiforgery", allowAnonymous: true);
        originalResponse.ReachedEndpoint.Should().BeTrue();
        var antiforgeryCookie = originalResponse.Context.Response.Headers.SetCookie.Single()!.Split(';')[0];
        var cookies = $"{login.Cookie}; {antiforgeryCookie}";
        await fixture.ChangeUsersAsync(async db =>
        {
            var target = await db.Users.SingleAsync(user => user.IamId == Fixture.TargetIamId);
            target.IsActive = false;
        });

        var staleRequest = await fixture.RequestAsync(cookies, selection, async context =>
        {
            context.Request.Headers[originalTokens!.HeaderName!] = originalTokens.RequestToken;
            var validate = () => context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            await validate.Should().ThrowAsync<AntiforgeryValidationException>();
        }, path: recoveryPath, method: "POST");
        staleRequest.ReachedEndpoint.Should().BeTrue();

        AntiforgeryTokenSet? refreshedTokens = null;
        var refreshedResponse = await fixture.RequestAsync(cookies, selection, context =>
        {
            refreshedTokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            return Task.CompletedTask;
        }, path: "/api/antiforgery", allowAnonymous: true);
        refreshedResponse.ReachedEndpoint.Should().BeTrue();
        refreshedResponse.Context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("emulation-unavailable");

        var recovery = await fixture.RequestAsync(cookies, selection, async context =>
        {
            context.Request.Headers[refreshedTokens!.HeaderName!] = refreshedTokens.RequestToken;
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }, path: recoveryPath, method: "POST");

        recovery.ReachedEndpoint.Should().BeTrue();
        recovery.Context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_sign_in_clears_selection_and_rotates_the_login_binding(bool local)
    {
        using var fixture = new Fixture(local);
        await fixture.SeedAsync();
        var firstLogin = await fixture.SignInAsync();
        AssertSelectionCleared(firstLogin.Context);
        var selection = fixture.Select(firstLogin.SessionId);

        var secondLogin = await fixture.SignInAsync(selection, firstLogin.SessionId);

        AssertSelectionCleared(secondLogin.Context);
        secondLogin.SessionId.Should().NotBeNullOrEmpty().And.NotBe(firstLogin.SessionId);
        AssertBlocked(await fixture.RequestAsync(secondLogin.Cookie, selection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_sign_out_clears_selection_and_uses_the_original_login(bool local)
    {
        using var fixture = new Fixture(local);
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();

        var result = await fixture.RequestAsync(login.Cookie, fixture.Select(login.SessionId), async context =>
        {
            context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.TargetIamId);
            await context.SignOutAsync(fixture.Scheme);
            context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
        }, path: "/logout");

        result.ReachedEndpoint.Should().BeTrue();
        AssertSelectionCleared(result.Context);
        result.Context.Response.Headers.SetCookie.Should().Contain(value => value!.StartsWith(fixture.CookieName + "=;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_sign_out_accepts_the_login_page_antiforgery_token_while_emulating(bool expiredSelection)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();
        var selection = fixture.Select(login.SessionId);
        if (expiredSelection)
        {
            selection = fixture.ExpireSelection(selection);
        }

        AntiforgeryTokenSet? tokens = null;
        var loginPage = await fixture.RequestAsync(login.Cookie, selection, context =>
        {
            tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            return Task.CompletedTask;
        }, path: "/login", allowAnonymous: true);
        loginPage.ReachedEndpoint.Should().BeTrue();
        var antiforgeryCookie = loginPage.Context.Response.Headers.SetCookie.Single()!.Split(';')[0];

        var result = await fixture.RequestAsync($"{login.Cookie}; {antiforgeryCookie}", selection, async context =>
        {
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                [tokens!.FormFieldName] = tokens.RequestToken,
            });
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
            EmulationService.IsEmulating(context).Should().BeFalse();
            await context.SignOutAsync(fixture.Scheme);
        }, path: "/logout/local", allowAnonymous: true, method: "POST");

        result.ReachedEndpoint.Should().BeTrue();
        AssertSelectionCleared(result.Context);
        result.Context.Response.Headers.SetCookie.Should().Contain(value => value!.StartsWith(fixture.CookieName + "=;"));
    }

    [Fact]
    public async Task Login_recovery_operates_on_the_real_identity_even_with_an_invalid_selection()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var login = await fixture.SignInAsync();

        var result = await fixture.RequestAsync(login.Cookie, Fixture.SelectionCookieName + "=invalid-ticket", path: "/login/local");

        result.ReachedEndpoint.Should().BeTrue();
        result.Context.User.FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
        EmulationService.IsEmulating(result.Context).Should().BeFalse();
    }

    [Fact]
    public async Task Existing_authentication_tickets_bind_selection_to_their_issue_time()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAt.ToUnixTimeSeconds());
        var cookie = fixture.CreateLegacyCookie(issuedAt);
        var selection = fixture.Select(issuedAt.ToString("O", CultureInfo.InvariantCulture));

        (await fixture.RequestAsync(cookie, selection)).ReachedEndpoint.Should().BeTrue();
        AssertBlocked(await fixture.RequestAsync(fixture.CreateLegacyCookie(issuedAt.AddSeconds(1)), selection));
    }

    [Fact]
    public async Task Authentication_without_a_login_binding_cannot_apply_an_emulation_selection()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();

        var result = await fixture.RequestAsync(fixture.CreateLegacyCookie(null), fixture.Select("unknown-session"));

        AssertBlocked(result);
    }

    private static void AssertBlocked((DefaultHttpContext Context, bool ReachedEndpoint) result)
    {
        result.ReachedEndpoint.Should().BeFalse();
        result.Context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        EmulationService.IsEmulating(result.Context).Should().BeTrue();
        result.Context.User.FindFirst("ucdPersonIAMID").Should().BeNull();
        result.Context.User.FindAll(ClaimTypes.Role).Should().BeEmpty();
        EmulationService.GetActor(result.Context).FindFirst("ucdPersonIAMID")!.Value.Should().Be(Fixture.ActorIamId);
    }

    private static void AssertSelectionCleared(HttpContext context)
        => context.Response.Headers.SetCookie.Should().Contain(value => value!.StartsWith(Fixture.SelectionCookieName + "=;"));

    private sealed class Fixture : IDisposable
    {
        public const string ActorIamId = "actor-10001";
        public const string TargetIamId = "target-10002";
        public const string SelectionCookieName = ".Booking.Emulation";
        private readonly ServiceProvider _provider;
        public FakeRosettaService Rosetta { get; } = new();
        public string Scheme { get; }
        public string CookieName => _provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).Cookie.Name!;

        public Fixture(bool local = true)
        {
            Scheme = local ? LocalAuthentication.Scheme : CookieAuthenticationDefaults.AuthenticationScheme;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:UseLocal"] = local.ToString(),
                ["Auth:Instance"] = "https://login.microsoftonline.com/",
                ["Auth:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["Auth:ClientId"] = "22222222-2222-2222-2222-222222222222",
            }).Build();
            var environment = new TestEnvironment { EnvironmentName = local ? Environments.Development : Environments.Production };
            var databaseName = "EmulationMiddleware_" + Guid.NewGuid().ToString("N");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting();
            services.AddAntiforgery();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddScoped<IUserService, UserService>();
            services.AddSingleton<IRosettaService>(Rosetta);
            services.AddAuthenticationServices(configuration, environment);
            _provider = services.BuildServiceProvider();
        }

        public Task SeedAsync() => ChangeUsersAsync(db =>
        {
            db.Users.Add(new User { IamId = ActorIamId, Name = "Actual Administrator", IsAdmin = true });
            db.TeamPermissions.Add(new TeamPermission
            {
                User = new User { IamId = TargetIamId, Name = "Target User", Email = "target@example.test" },
                Team = new Team { Name = "Target Team", Slug = "target-team" },
                Role = TeamRole.Viewer,
            });
            db.Teams.Add(new Team { Name = "Actor Team", Slug = "actor-team" });
            return Task.CompletedTask;
        });

        public async Task ChangeUsersAsync(Func<AppDbContext, Task> change)
        {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await change(db);
            await db.SaveChangesAsync();
        }

        public static ClaimsPrincipal Actor(string iamId = ActorIamId) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "real-login:" + iamId),
            new Claim("ucdPersonIAMID", iamId),
            new Claim("name", "Actual Administrator"),
            new Claim(ClaimTypes.Role, "ActorOnlyRole"),
            new Claim("actor-only", "private"),
        ], "Test", "name", ClaimTypes.Role));

        public async Task<(string Cookie, string SessionId, DefaultHttpContext Context)> SignInAsync(
            string? selection = null, string? previousSessionId = null)
        {
            using var scope = _provider.CreateScope();
            var context = CreateContext(scope.ServiceProvider, "/login", selection);
            var properties = new AuthenticationProperties();
            if (previousSessionId != null)
            {
                properties.Items[EmulationService.SessionPropertyKey] = previousSessionId;
            }
            await context.SignInAsync(Scheme, Actor(), properties);
            var cookie = GetCookie(context, CookieName);
            var ticket = CookieFormat.Unprotect(Uri.UnescapeDataString(cookie.Split('=', 2)[1]))!;
            return (cookie, ticket.Properties.Items[EmulationService.SessionPropertyKey]!, context);
        }

        public string Select(string sessionId, ClaimsPrincipal? actor = null)
        {
            using var scope = _provider.CreateScope();
            var context = CreateContext(scope.ServiceProvider, "/api/emulation/start");
            var target = scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Single(user => user.IamId == TargetIamId);
            scope.ServiceProvider.GetRequiredService<EmulationService>().Start(context, actor ?? Actor(), sessionId, target);
            return GetCookie(context, SelectionCookieName);
        }

        public string ExpireSelection(string selection)
        {
            var format = new TicketDataFormat(_provider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Booking.Emulation.v1", SelectionCookieName));
            var ticket = format.Unprotect(Uri.UnescapeDataString(selection.Split('=', 2)[1]))!;
            ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            return SelectionCookieName + "=" + format.Protect(ticket);
        }

        public string CreateLegacyCookie(DateTimeOffset? issuedAt)
        {
            var ticket = new AuthenticationTicket(Actor(), new AuthenticationProperties
            {
                IssuedUtc = issuedAt,
                ExpiresUtc = (issuedAt ?? DateTimeOffset.UtcNow).AddHours(1),
            }, Scheme);
            return CookieName + "=" + CookieFormat.Protect(ticket);
        }

        private ISecureDataFormat<AuthenticationTicket> CookieFormat
            => _provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).TicketDataFormat;

        public async Task<(DefaultHttpContext Context, bool ReachedEndpoint)> RequestAsync(
            string? loginCookie, string? selection = null, RequestDelegate? endpoint = null,
            string path = "/api/example", string? policy = null, string? teamSlug = null, bool requireAuthentication = true,
            bool allowAnonymous = false, string method = "GET")
        {
            using var scope = _provider.CreateScope();
            var cookies = string.Join("; ", new[] { loginCookie, selection }.Where(cookie => cookie != null));
            var context = CreateContext(scope.ServiceProvider, path, cookies);
            context.Request.Method = method;
            if (teamSlug != null)
            {
                context.Request.RouteValues["teamSlug"] = teamSlug;
            }
            var metadata = new List<object>();
            if (requireAuthentication)
            {
                metadata.Add(new AuthorizeAttribute { Policy = policy });
            }
            if (allowAnonymous)
            {
                metadata.Add(new AllowAnonymousAttribute());
            }
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "Emulation test endpoint"));
            var reachedEndpoint = false;
            var builder = new ApplicationBuilder(_provider);
            builder.UseAuthentication();
            builder.UseMiddleware<EmulationMiddleware>();
            builder.UseAuthorization();
            builder.Run(async request =>
            {
                reachedEndpoint = true;
                if (endpoint != null)
                {
                    await endpoint(request);
                }
            });
            await builder.Build()(context);
            return (context, reachedEndpoint);
        }

        private static DefaultHttpContext CreateContext(IServiceProvider services, string path, string? cookies = null)
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Scheme = "https";
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            if (!string.IsNullOrEmpty(cookies))
            {
                context.Request.Headers.Cookie = cookies;
            }
            return context;
        }

        private static string GetCookie(HttpContext context, string name)
            => context.Response.Headers.SetCookie.Single(value => value!.StartsWith(name + "="))!.Split(';')[0];

        public void Dispose() => _provider.Dispose();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

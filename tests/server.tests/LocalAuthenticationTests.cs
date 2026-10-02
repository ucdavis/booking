using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Server.Controllers;
using Server.Core.Domain;
using Server.Helpers;

namespace Server.Tests;

public class LocalAuthenticationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("test")]
    [InlineData("Staging")]
    public void Local_auth_is_rejected_outside_development(string environment)
    {
        var configure = () => new ServiceCollection().AddAuthenticationServices(
            Configuration(local: "true"), new TestEnvironment(environment));

        configure.Should().Throw<InvalidOperationException>().WithMessage("*only allowed*Development*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Entra_remains_the_default_when_local_auth_is_not_enabled(string? local)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthenticationServices(Configuration(local, "11111111-1111-1111-1111-111111111111"),
            new TestEnvironment("Development"));
        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);
        (await schemes.GetSchemeAsync(LocalAuthentication.Scheme)).Should().BeNull();
    }

    [Fact]
    public void Entra_still_requires_a_real_client_id()
    {
        var configure = () => new ServiceCollection().AddAuthenticationServices(
            Configuration(), new TestEnvironment("Development"));

        configure.Should().Throw<InvalidOperationException>().WithMessage("Auth:ClientId is not configured.*");
    }

    [Fact]
    public async Task Local_auth_does_not_register_Entra_or_need_its_configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthenticationServices(Configuration("true"), new TestEnvironment("Development"));
        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(LocalAuthentication.Scheme);
        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme)).Should().BeNull();
    }

    [Fact]
    public void Personas_exercise_the_existing_role_boundary()
    {
        var sample = LocalAuthentication.CreatePrincipal("sample")!;
        var basic = LocalAuthentication.CreatePrincipal("basic")!;

        sample.Identity!.IsAuthenticated.Should().BeTrue();
        sample.IsInRole("User").Should().BeTrue();
        sample.IsInRole("SampleRole").Should().BeTrue();
        basic.IsInRole("User").Should().BeTrue();
        basic.IsInRole("SampleRole").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("admin")]
    [InlineData("sample,Admin")]
    public void Login_rejects_unknown_personas(string? persona)
    {
        LocalAuthentication.CreatePrincipal(persona).Should().BeNull();
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    public void Login_does_not_redirect_to_an_external_site(string returnUrl)
    {
        var controller = Controller(local: "true");

        var view = controller.Login(returnUrl).Should().BeOfType<ViewResult>().Subject;

        view.Model.Should().Be("/");
    }

    [Fact]
    public void Normal_login_challenges_Entra_with_the_local_return_url()
    {
        var controller = Controller();

        var challenge = controller.Login("/fetch?example=1").Should().BeOfType<ChallengeResult>().Subject;

        challenge.AuthenticationSchemes.Should().ContainSingle().Which.Should().Be(OpenIdConnectDefaults.AuthenticationScheme);
        challenge.Properties!.RedirectUri.Should().Be("/fetch?example=1");
    }

    [Fact]
    public async Task Local_login_and_logout_are_unavailable_by_default()
    {
        var controller = Controller();

        (await controller.LocalLogin("sample", "/")).Should().BeOfType<NotFoundResult>();
        (await controller.LocalLogout()).Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(nameof(AccountController.LocalLogin), "login/local")]
    [InlineData(nameof(AccountController.LocalPersonLogin), "login/local/person")]
    [InlineData(nameof(AccountController.LocalLogout), "logout/local")]
    public void Local_sign_in_and_out_require_antiforgery_protected_posts(string actionName, string route)
    {
        var action = typeof(AccountController).GetMethod(actionName)!;

        action.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be(route);
        typeof(AccountController).GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>().Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task People_login_is_unavailable_unless_local_auth_is_enabled(string? local)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var controller = Controller(local);

        (await controller.LocalPersonLogin("10010001", "/", db)).Should().BeOfType<NotFoundResult>();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("10010001")]
    [InlineData("jordan@example.test")]
    [InlineData("jdemo")]
    public async Task People_login_uses_an_exact_directory_identifier_and_preserves_local_return_urls(string query)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(Person());
        await db.SaveChangesAsync();
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin($"  {query}  ", "/teams/demo/members?view=all", db);

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/teams/demo/members?view=all");
        authentication.SignInCount.Should().Be(1);
        authentication.Scheme.Should().Be(LocalAuthentication.Scheme);
        authentication.Principal!.FindFirst("ucdPersonIAMID")!.Value.Should().Be("10010001");
        authentication.Principal.Identity!.Name.Should().Be("Jordan Demo");
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    public async Task People_login_does_not_redirect_to_an_external_site(string returnUrl)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(Person());
        await db.SaveChangesAsync();
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin("10010001", returnUrl, db);

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/");
        authentication.SignInCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Jordan Demo")]
    [InlineData("jordan")]
    [InlineData("missing@example.test")]
    public async Task People_login_rejects_empty_missing_and_partial_matches_without_signing_in(string? query)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(Person());
        await db.SaveChangesAsync();
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "https://example.com/", db);

        AssertPeopleLoginError(controller, result, authentication, "/");
        controller.ViewData["PeopleQuery"].Should().Be(query?.Trim());
    }

    [Fact]
    public async Task People_login_rejects_queries_longer_than_the_directory_fields()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var query = new string('a', 129);
        db.People.Add(new Person { IamId = "10010001", Email = query, IsActiveInIam = true });
        await db.SaveChangesAsync();
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "/teams/demo", db);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task People_login_rejects_ambiguous_matches_instead_of_selecting_a_person(bool differentFields, bool secondIsActive)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var first = Person();
        var second = new Person { IamId = "10010002", FullName = "Another Person", IsActiveInIam = secondIsActive };
        var query = differentFields ? first.IamId : first.Email!;
        if (differentFields)
        {
            second.UserId = query;
        }
        else
        {
            second.Email = query;
        }
        db.People.AddRange(first, second);
        await db.SaveChangesAsync();
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "/teams/demo", db);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task People_login_rejects_inactive_directory_and_application_users(bool isActiveInIam, bool isActiveUser)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var person = Person();
        person.IsActiveInIam = isActiveInIam;
        db.People.Add(person);
        db.Users.Add(new User { IamId = person.IamId, Name = "Existing User", IsActive = isActiveUser, IsAdmin = true });
        await db.SaveChangesAsync();
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(person.IamId, "/teams/demo", db);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
        (await db.Users.SingleAsync()).IsActive.Should().Be(isActiveUser);
    }

    [Fact]
    public void Directory_principal_uses_only_directory_identity_claims_and_the_user_role()
    {
        var person = Person();
        person.IamId = " 10010001 ";
        person.FullName = " Jordan Demo ";
        person.Email = " jordan@example.test ";

        var principal = LocalAuthentication.CreatePersonPrincipal(person)!;

        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.Identity.AuthenticationType.Should().Be(LocalAuthentication.Scheme);
        principal.Identity.Name.Should().Be("Jordan Demo");
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("local-person:10010001");
        principal.FindFirst("ucdPersonIAMID")!.Value.Should().Be("10010001");
        principal.FindFirst("name")!.Value.Should().Be("Jordan Demo");
        principal.FindFirst("preferred_username")!.Value.Should().Be("jordan@example.test");
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Should().Equal("User");
    }

    [Theory]
    [InlineData(null, " Jordan ", " Demo ", "Jordan Demo")]
    [InlineData(" ", " Jordan ", null, "Jordan")]
    [InlineData(null, null, " Demo ", "Demo")]
    [InlineData(null, " ", null, "10010001")]
    public void Directory_principal_has_a_name_fallback_and_omits_empty_email(
        string? fullName, string? firstName, string? lastName, string expectedName)
    {
        var person = new Person
        {
            IamId = "10010001", FullName = fullName, FirstName = firstName, LastName = lastName,
            Email = " ", IsActiveInIam = true,
        };

        var principal = LocalAuthentication.CreatePersonPrincipal(person)!;

        principal.Identity!.Name.Should().Be(expectedName);
        principal.FindFirst("name")!.Value.Should().Be(expectedName);
        principal.FindFirst("preferred_username").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Directory_principal_requires_an_iam_identifier(string iamId)
    {
        LocalAuthentication.CreatePersonPrincipal(new Person { IamId = iamId, IsActiveInIam = true }).Should().BeNull();
    }

    [Fact]
    public void Directory_principal_rejects_an_inactive_person()
    {
        var person = Person();
        person.IsActiveInIam = false;

        LocalAuthentication.CreatePersonPrincipal(person).Should().BeNull();
    }

    private static Person Person() => new()
    {
        IamId = "10010001", FullName = "Jordan Demo", Email = "jordan@example.test", UserId = "jdemo", IsActiveInIam = true,
    };

    private static void AssertPeopleLoginError(
        AccountController controller, IActionResult result, RecordingAuthenticationService authentication, string returnUrl)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("LocalLogin");
        view.Model.Should().Be(returnUrl);
        (view.StatusCode ?? controller.Response.StatusCode).Should().Be(StatusCodes.Status400BadRequest);
        controller.ModelState["query"]!.Errors.Should().NotBeEmpty();
        authentication.SignInCount.Should().Be(0);
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    private static AccountController Controller(string? local = null, IServiceProvider? services = null)
    {
        var controller = new AccountController(Configuration(local), new TestEnvironment("Development"))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
                RouteData = new RouteData(),
            },
        };
        if (services != null)
        {
            controller.HttpContext.RequestServices = services;
        }
        controller.TempData = new TempDataDictionary(controller.HttpContext, new EmptyTempDataProvider());
        controller.Url = new UrlHelper(controller.ControllerContext);
        return controller;
    }

    private static IConfiguration Configuration(string? local = null, string clientId = "<client-guid>") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = local,
            ["Auth:ClientId"] = clientId,
        }).Build();

    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public int SignInCount { get; private set; }
        public string? Scheme { get; private set; }
        public ClaimsPrincipal? Principal { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignInCount++;
            Scheme = scheme;
            Principal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

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
using Microsoft.Extensions.Options;
using Server.Controllers;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Directory;

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
    public async Task Configured_Entra_uses_the_local_cookie_without_changing_the_local_defaults()
    {
        var configuration = Configuration("true", "11111111-1111-1111-1111-111111111111");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthenticationServices(configuration, new TestEnvironment("Development"));
        await using var provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name.Should().Be(LocalAuthentication.Scheme);
        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(LocalAuthentication.Scheme);
        (await schemes.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme)).Should().NotBeNull();
        (await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Should().BeNull();
        provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme).SignInScheme.Should().Be(LocalAuthentication.Scheme);
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

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://example.test/phishing", "/")]
    [InlineData("//example.test/phishing", "/")]
    [InlineData("/\\example.test/phishing", "/")]
    [InlineData("/teams/demo?view=all", "/teams/demo?view=all")]
    public void Normal_login_bypasses_the_local_chooser_and_only_accepts_local_return_urls(
        string? returnUrl, string expectedUrl)
    {
        var controller = Controller("true", clientId: "11111111-1111-1111-1111-111111111111");
        controller.HttpContext.User = LocalAuthentication.CreatePrincipal("basic")!;

        var challenge = controller.NormalLogin(returnUrl).Should().BeOfType<ChallengeResult>().Subject;

        challenge.AuthenticationSchemes.Should().Equal(OpenIdConnectDefaults.AuthenticationScheme);
        challenge.Properties!.RedirectUri.Should().Be(expectedUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("<client-guid>")]
    [InlineData(" <CLIENT-GUID> ")]
    public void Unconfigured_normal_login_keeps_the_local_chooser_available(string clientId)
    {
        var controller = Controller("true", clientId: clientId);

        var view = controller.NormalLogin("https://example.test/phishing").Should().BeOfType<ViewResult>().Subject;

        view.ViewName.Should().Be("LocalLogin");
        view.Model.Should().Be("/");
        view.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        view.ViewData["NormalLoginAvailable"].Should().Be(false);
        controller.ModelState.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("<client-guid>", false)]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    public void Local_chooser_reports_whether_normal_login_is_available(string clientId, bool available)
    {
        var controller = Controller("true", clientId: clientId);

        var view = controller.Login("/teams/demo").Should().BeOfType<ViewResult>().Subject;

        view.ViewData["NormalLoginAvailable"].Should().Be(available);
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

        var directory = new FakeRosettaService(db);
        (await controller.LocalPersonLogin("10010001", "/", db, directory)).Should().BeOfType<NotFoundResult>();
        directory.SearchCalls.Should().BeEmpty();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("10010001")]
    [InlineData("jordan@example.test")]
    [InlineData("jdemo")]
    public async Task People_login_uses_an_exact_directory_identifier_and_preserves_local_return_urls(string query)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var directory = new FakeRosettaService(db);
        directory.People.Add(Person());
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin($"  {query}  ", "/teams/demo/members?view=all", db, directory);

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/teams/demo/members?view=all");
        authentication.SignInCount.Should().Be(1);
        authentication.Scheme.Should().Be(LocalAuthentication.Scheme);
        authentication.Principal!.FindFirst("ucdPersonIAMID")!.Value.Should().Be("10010001");
        authentication.Principal.Identity!.Name.Should().Be("Jordan Demo");
        authentication.Principal.FindFirst(LocalAuthentication.KerberosClaimType)!.Value.Should().Be("jdemo");
        directory.SearchCalls.Should().Equal(query);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    public async Task People_login_does_not_redirect_to_an_external_site(string returnUrl)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var directory = new FakeRosettaService(db);
        directory.People.Add(Person());
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin("10010001", returnUrl, db, directory);

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
        var directory = new FakeRosettaService(db);
        directory.People.Add(Person());
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "https://example.com/", db, directory);

        AssertPeopleLoginError(controller, result, authentication, "/");
        controller.ViewData["PeopleQuery"].Should().Be(query?.Trim());
    }

    [Fact]
    public async Task People_login_rejects_queries_longer_than_the_directory_fields()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var query = new string('a', 129);
        var directory = new FakeRosettaService(db);
        directory.People.Add(new DirectoryPerson { IamId = "10010001", Name = "Jordan Demo", Email = query, Kerberos = "jdemo" });
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "/teams/demo", db, directory);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
        directory.SearchCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task People_login_rejects_ambiguous_matches_instead_of_selecting_a_person(bool differentFields)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var first = Person();
        var second = new DirectoryPerson { IamId = "10010002", Name = "Another Person", Email = "another@example.test", Kerberos = "another" };
        var query = differentFields ? first.IamId : first.Email!;
        if (differentFields)
        {
            second.Kerberos = query;
        }
        else
        {
            second.Email = query;
        }
        var directory = new FakeRosettaService(db);
        directory.People.AddRange([first, second]);
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "/teams/demo", db, directory);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
    }

    [Theory]
    [InlineData("iam")]
    [InlineData("email")]
    [InlineData("kerberos")]
    public async Task People_login_requires_a_complete_directory_profile_for_a_new_user(string missingField)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var person = Person();
        if (missingField == "iam") person.IamId = " ";
        if (missingField == "email") person.Email = null;
        if (missingField == "kerberos") person.Kerberos = " ";
        var directory = new FakeRosettaService(db);
        directory.People.Add(person);
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var query = missingField == "iam" ? person.Email : person.IamId;
        var result = await controller.LocalPersonLogin(query, "/teams/demo", db, directory);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task People_login_rejects_an_inactive_application_user()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var person = Person();
        db.Users.Add(new User { IamId = person.IamId, Name = "Existing User", IsActive = false, IsAdmin = true });
        await db.SaveChangesAsync();
        var directory = new FakeRosettaService(db);
        directory.People.Add(person);
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(person.IamId, "/teams/demo", db, directory);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
        (await db.Users.SingleAsync()).IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("10010001")]
    [InlineData("existing@ucdavis.edu")]
    [InlineData("existingkerb")]
    public async Task People_login_uses_an_active_existing_account_without_Rosetta_configuration(string query)
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User
        {
            IamId = "10010001", Name = "Existing User", Email = "existing@ucdavis.edu",
            Kerberos = "existingkerb", IsActive = true, IsAdmin = true,
        });
        await db.SaveChangesAsync();
        var directory = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable") };
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin(query, "/teams/demo", db, directory);

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/teams/demo");
        authentication.SignInCount.Should().Be(1);
        authentication.Principal!.Identity!.Name.Should().Be("Existing User");
        authentication.Principal.FindFirst("preferred_username")!.Value.Should().Be("existing@ucdavis.edu");
        authentication.Principal.FindFirst(LocalAuthentication.KerberosClaimType)!.Value.Should().Be("existingkerb");
        authentication.Principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Should().Equal("User");
    }

    [Fact]
    public async Task People_login_reports_directory_failure_without_signing_in_or_exposing_exception_details()
    {
        await using var db = TestDbContextFactory.CreateInMemory();
        var directory = new FakeRosettaService(db) { Failure = new HttpRequestException("private upstream details") };
        var authentication = new RecordingAuthenticationService();
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var controller = Controller("true", services);

        var result = await controller.LocalPersonLogin("jdemo", "/teams/demo", db, directory);

        AssertPeopleLoginError(controller, result, authentication, "/teams/demo");
        controller.ModelState["query"]!.Errors.Single().ErrorMessage.Should().Be("The directory could not be reached. Try again later.");
    }

    [Fact]
    public void Directory_principal_uses_only_directory_identity_claims_and_the_user_role()
    {
        var person = Person();
        person.IamId = " 10010001 ";
        person.Name = " Jordan Demo ";
        person.Email = " jordan@example.test ";
        person.Kerberos = " jdemo ";

        var principal = LocalAuthentication.CreatePersonPrincipal(person)!;

        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.Identity.AuthenticationType.Should().Be(LocalAuthentication.Scheme);
        principal.Identity.Name.Should().Be("Jordan Demo");
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("local-person:10010001");
        principal.FindFirst("ucdPersonIAMID")!.Value.Should().Be("10010001");
        principal.FindFirst("name")!.Value.Should().Be("Jordan Demo");
        principal.FindFirst("preferred_username")!.Value.Should().Be("jordan@example.test");
        principal.FindFirst(LocalAuthentication.KerberosClaimType)!.Value.Should().Be("jdemo");
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Should().Equal("User");
    }

    [Theory]
    [InlineData(" Jordan Demo ", "Jordan Demo")]
    [InlineData(" ", "10010001")]
    public void Directory_principal_has_a_name_fallback(
        string name, string expectedName)
    {
        var person = new DirectoryPerson
        {
            IamId = "10010001", Name = name,
            Email = "jordan@example.test", Kerberos = "jdemo",
        };

        var principal = LocalAuthentication.CreatePersonPrincipal(person)!;

        principal.Identity!.Name.Should().Be(expectedName);
        principal.FindFirst("name")!.Value.Should().Be(expectedName);
        principal.FindFirst("preferred_username")!.Value.Should().Be("jordan@example.test");
        principal.FindFirst(LocalAuthentication.KerberosClaimType)!.Value.Should().Be("jdemo");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Directory_principal_requires_an_iam_identifier(string iamId)
    {
        var person = Person();
        person.IamId = iamId;

        LocalAuthentication.CreatePersonPrincipal(person).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "jdemo")]
    [InlineData(" ", "jdemo")]
    [InlineData("jordan@example.test", null)]
    [InlineData("jordan@example.test", " ")]
    public void Directory_principal_requires_email_and_kerberos(string? email, string? kerberos)
    {
        var person = Person();
        person.Email = email;
        person.Kerberos = kerberos;

        LocalAuthentication.CreatePersonPrincipal(person).Should().BeNull();
    }

    private static DirectoryPerson Person() => new()
    {
        IamId = "10010001", Name = "Jordan Demo", Email = "jordan@example.test", Kerberos = "jdemo",
    };

    private static void AssertPeopleLoginError(
        AccountController controller, IActionResult result, RecordingAuthenticationService authentication, string returnUrl)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("LocalLogin");
        view.Model.Should().Be(returnUrl);
        (view.StatusCode ?? controller.Response.StatusCode).Should().Be(StatusCodes.Status400BadRequest);
        controller.ModelState["query"]!.Errors.Should().NotBeEmpty();
        view.ViewData["NormalLoginAvailable"].Should().Be(false);
        authentication.SignInCount.Should().Be(0);
        controller.Response.Headers.SetCookie.Should().BeEmpty();
    }

    private static AccountController Controller(string? local = null, IServiceProvider? services = null,
        string clientId = "<client-guid>")
    {
        var controller = new AccountController(Configuration(local, clientId), new TestEnvironment("Development"))
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
            ["Auth:Instance"] = "https://login.microsoftonline.com/",
            ["Auth:TenantId"] = "11111111-1111-1111-1111-111111111111",
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

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Server.Controllers;
using Server.Helpers;

namespace Server.Tests.Controllers;

public class AccountControllerTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://example.test/phishing", "/")]
    [InlineData("//example.test/phishing", "/")]
    [InlineData("/\\example.test/phishing", "/")]
    [InlineData("/fetch?sort=date", "/fetch?sort=date")]
    [InlineData("~/me", "~/me")]
    public void Login_only_redirects_to_local_paths(string? returnUrl, string expectedUrl)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([], "TestAuth")),
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var controller = new AccountController(new ConfigurationBuilder().Build(), new TestEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            Url = new UrlHelper(actionContext),
        };

        var result = controller.Login(returnUrl);

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be(expectedUrl);
    }

    [Fact]
    public void Local_logout_clears_the_local_cookie_and_returns_to_sign_in()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:UseLocal"] = "true" })
            .Build();
        var controller = new AccountController(configuration, new TestEnvironment());

        var result = controller.Logout().Should().BeOfType<SignOutResult>().Subject;

        result.AuthenticationSchemes.Should().Equal(LocalAuthentication.Scheme);
        result.Properties!.RedirectUri.Should().Be("/login");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Entra_logout_clears_the_cookie_and_signs_out_of_the_identity_provider(string? local)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:UseLocal"] = local })
            .Build();
        var controller = new AccountController(configuration, new TestEnvironment());

        var result = controller.Logout().Should().BeOfType<SignOutResult>().Subject;

        result.AuthenticationSchemes.Should().Equal(
            CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
        result.Properties!.RedirectUri.Should().Be("/about");
    }

    [Fact]
    public void Logout_requires_an_antiforgery_protected_post()
    {
        var action = typeof(AccountController).GetMethod(nameof(AccountController.Logout))!;

        action.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("logout");
        typeof(AccountController).GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("TRACE", false)]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    public async Task Account_antiforgery_filter_only_requires_tokens_for_unsafe_methods(string method, bool requiresToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        using var provider = services.BuildServiceProvider();
        var attribute = typeof(AccountController).GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>()!;
        var filter = attribute.CreateInstance(provider);
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Method = method;
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new AuthorizationFilterContext(actionContext, [filter]);

        await ((IAsyncAuthorizationFilter)filter).OnAuthorizationAsync(context);

        if (requiresToken)
        {
            context.Result.Should().BeOfType<AntiforgeryValidationFailedResult>();
        }
        else
        {
            context.Result.Should().BeNull();
        }
    }

    [Fact]
    public void Antiforgery_tokens_are_available_through_the_shared_and_logout_routes()
    {
        var action = typeof(AccountController).GetMethod(nameof(AccountController.LogoutAntiforgery))!;

        action.GetCustomAttributes<HttpGetAttribute>().Select(attribute => attribute.Template)
            .Should().BeEquivalentTo(new[] { "api/antiforgery", "logout/antiforgery" });
    }

    [Fact]
    public void Logout_antiforgery_returns_the_configured_form_field_and_stores_a_cookie()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery(options => options.FormFieldName = "LogoutRequestToken");
        using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var controller = new AccountController(new ConfigurationBuilder().Build(), new TestEnvironment())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = controller.LogoutAntiforgery(provider.GetRequiredService<IAntiforgery>())
            .Should().BeOfType<OkObjectResult>().Subject;
        var response = JsonSerializer.SerializeToElement(result.Value);

        response.GetProperty("FormFieldName").GetString().Should().Be("LogoutRequestToken");
        response.GetProperty("RequestToken").GetString().Should().NotBeNullOrWhiteSpace();
        httpContext.Response.Headers.SetCookie.Should().NotBeEmpty();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

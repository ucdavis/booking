using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Helpers;
using Server.Models.Directory;
using Server.Services;

namespace Server.Controllers;

[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController(IConfiguration configuration, IHostEnvironment environment) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (LocalAuthentication.IsEnabled(configuration, environment))
        {
            return LocalLoginView(safeReturnUrl);
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(safeReturnUrl);
        }

        return Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("login/entra")]
    public IActionResult NormalLogin(string? returnUrl)
    {
        if (LocalAuthentication.IsEnabled(configuration, environment) && !AuthenticationHelper.IsEntraConfigured(configuration))
        {
            ModelState.AddModelError(string.Empty, "Normal login is not configured for this development environment.");
            var view = LocalLoginView(returnUrl);
            view.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return view;
        }

        return Challenge(new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/" },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("login/local")]
    public async Task<IActionResult> LocalLogin(string? persona, string? returnUrl)
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        var principal = LocalAuthentication.CreatePrincipal(persona);
        if (principal == null)
        {
            return BadRequest("Choose one of the listed sandbox users.");
        }

        await HttpContext.SignInAsync(LocalAuthentication.Scheme, principal);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    [HttpGet("logout/antiforgery")]
    [HttpGet("api/antiforgery")]
    public IActionResult LogoutAntiforgery([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { tokens.FormFieldName, tokens.RequestToken });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (LocalAuthentication.IsEnabled(configuration, environment))
        {
            var actor = EmulationService.GetActor(HttpContext);
            if (actor.Identity?.IsAuthenticated == true && actor.Identity.AuthenticationType != LocalAuthentication.Scheme &&
                AuthenticationHelper.IsEntraConfigured(configuration))
            {
                return SignOut(new AuthenticationProperties { RedirectUri = "/login" },
                    LocalAuthentication.Scheme, OpenIdConnectDefaults.AuthenticationScheme);
            }

            return SignOut(new AuthenticationProperties { RedirectUri = "/login" }, LocalAuthentication.Scheme);
        }

        return SignOut(new AuthenticationProperties { RedirectUri = "/about" },
            CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("logout/local")]
    public Task<IActionResult> LocalLogout()
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return Task.FromResult<IActionResult>(NotFound());
        }

        return Task.FromResult(Logout());
    }

    [HttpPost("login/local/person")]
    public async Task<IActionResult> LocalPersonLogin(
        [FromForm] string? query, [FromForm] string? returnUrl, [FromServices] AppDbContext dbContext,
        [FromServices] IRosettaService rosettaService,
        CancellationToken cancellationToken = default)
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        var search = query?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length > 128)
        {
            return PersonLoginError(search, returnUrl, "Enter a complete email, IAM ID, or Kerberos ID of no more than 128 characters.");
        }

        IReadOnlyList<DirectoryPerson> people;
        try
        {
            people = await rosettaService.SearchPeopleAsync(search, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is InvalidOperationException)
        {
            return PersonLoginError(search, returnUrl, "The directory could not be reached. Try again later.");
        }
        if (people.Count == 0)
        {
            return PersonLoginError(search, returnUrl, "No person matched that email or ID. Check the identifier and try again.");
        }
        if (people.Count > 1)
        {
            return PersonLoginError(search, returnUrl, "More than one person matched. Enter their unique IAM ID instead.");
        }

        var person = people[0];
        var iamId = person.IamId.Trim();
        var existingUser = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        if (existingUser != null && !existingUser.IsActive)
        {
            return PersonLoginError(search, returnUrl, "This user's application account is inactive and cannot sign in.");
        }

        var principal = LocalAuthentication.CreatePersonPrincipal(person);
        if (principal == null)
        {
            return PersonLoginError(search, returnUrl, "This person does not have the IAM ID, Kerberos ID, and email required to sign in.");
        }

        // The normal local-cookie sign-in event saves the profile and preserves application permissions.
        await HttpContext.SignInAsync(LocalAuthentication.Scheme, principal);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    private ViewResult PersonLoginError(string? query, string? returnUrl, string message)
    {
        ViewData["PeopleQuery"] = query;
        ModelState.AddModelError("query", message);
        var view = LocalLoginView(returnUrl);
        view.StatusCode = StatusCodes.Status400BadRequest;
        return view;
    }

    private ViewResult LocalLoginView(string? returnUrl)
    {
        ViewData["NormalLoginAvailable"] = AuthenticationHelper.IsEntraConfigured(configuration);
        return View("LocalLogin", Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}

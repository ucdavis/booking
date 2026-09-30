using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Helpers;

namespace Server.Controllers;

[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController(IConfiguration configuration, IHostEnvironment environment) : Controller
{
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        if (LocalAuthentication.IsEnabled(configuration, environment))
        {
            return View("LocalLogin", safeReturnUrl);
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(safeReturnUrl);
        }

        return Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl },
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("login/local")]
    [ValidateAntiForgeryToken]
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

    [HttpPost("logout/local")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LocalLogout()
    {
        if (!LocalAuthentication.IsEnabled(configuration, environment))
        {
            return NotFound();
        }

        await HttpContext.SignOutAsync(LocalAuthentication.Scheme);
        return LocalRedirect("/login");
    }

    [HttpPost("login/local/person")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LocalPersonLogin(
        [FromForm] string? query, [FromForm] string? returnUrl, [FromServices] AppDbContext dbContext,
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

        var people = await dbContext.People.AsNoTracking()
            .Where(person => person.Email == search || person.IamId == search || person.UserId == search)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (people.Count == 0)
        {
            return PersonLoginError(search, returnUrl, "No person matched that email or ID. Check the People table and try again.");
        }
        if (people.Count > 1)
        {
            return PersonLoginError(search, returnUrl, "More than one person matched. Enter their unique IAM ID instead.");
        }

        var person = people[0];
        var principal = LocalAuthentication.CreatePersonPrincipal(person);
        if (principal == null)
        {
            return PersonLoginError(search, returnUrl, "This person is inactive in IAM and cannot sign in.");
        }

        var iamId = person.IamId.Trim();
        if (await dbContext.Users.AnyAsync(user => user.IamId == iamId && !user.IsActive, cancellationToken))
        {
            return PersonLoginError(search, returnUrl, "This user's application account is inactive and cannot sign in.");
        }

        // The normal local-cookie sign-in event saves the profile and preserves application permissions.
        await HttpContext.SignInAsync(LocalAuthentication.Scheme, principal);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    private ViewResult PersonLoginError(string? query, string? returnUrl, string message)
    {
        ViewData["PeopleQuery"] = query;
        ModelState.AddModelError("query", message);
        var view = View("LocalLogin", Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        view.StatusCode = StatusCodes.Status400BadRequest;
        return view;
    }
}

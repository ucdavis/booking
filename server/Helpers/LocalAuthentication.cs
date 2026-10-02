using System.Security.Claims;
using Server.Core.Domain;

namespace Server.Helpers;

public static class LocalAuthentication
{
    public const string Scheme = "LocalSandbox";

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("Auth:UseLocal");
        if (enabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Auth:UseLocal is only allowed in the Development environment.");
        }

        return enabled;
    }

    public static ClaimsPrincipal? CreatePrincipal(string? persona)
    {
        if (persona != "sample" && persona != "basic")
        {
            return null;
        }

        var isSample = persona == "sample";
        var name = isSample ? "Sample User" : "Basic User";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, isSample ? "sandbox-sample" : "sandbox-basic"),
            new(ClaimTypes.Name, name),
            new("name", name),
            new("preferred_username", $"{persona}@example.test"),
            new("ucdPersonIAMID", isSample ? "sandbox-10001" : "sandbox-10002"),
            new(ClaimTypes.Role, "User"),
        };

        if (isSample)
        {
            claims.Add(new Claim(ClaimTypes.Role, "SampleRole"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }

    public static ClaimsPrincipal? CreatePersonPrincipal(Person person)
    {
        var iamId = person.IamId.Trim();
        if (!person.IsActiveInIam || string.IsNullOrWhiteSpace(iamId))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(person.FullName)
            ? $"{person.FirstName?.Trim()} {person.LastName?.Trim()}".Trim()
            : person.FullName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = iamId;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"local-person:{iamId}"),
            new(ClaimTypes.Name, name),
            new("name", name),
            new("ucdPersonIAMID", iamId),
            new(ClaimTypes.Role, "User"),
        };
        if (!string.IsNullOrWhiteSpace(person.Email))
        {
            claims.Add(new Claim("preferred_username", person.Email.Trim()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }
}

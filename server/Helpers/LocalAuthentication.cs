using System.Security.Claims;
using Server.Models.Directory;

namespace Server.Helpers;

public static class LocalAuthentication
{
    public const string Scheme = "LocalSandbox";
    public const string KerberosClaimType = "kerberos";

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

    public static ClaimsPrincipal? CreatePersonPrincipal(DirectoryPerson person, bool hasActiveAccount = false)
    {
        var iamId = person.IamId.Trim();
        if ((!hasActiveAccount && person.IsActiveInIam != true) || string.IsNullOrWhiteSpace(iamId))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(person.Name) ? iamId : person.Name.Trim();

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
        if (!string.IsNullOrWhiteSpace(person.Kerberos))
        {
            claims.Add(new Claim(KerberosClaimType, person.Kerberos.Trim()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }
}

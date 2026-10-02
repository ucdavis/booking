using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;

namespace Server.Services;

public interface IUserService
{
    Task<bool> IsSiteAdmin(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task UpdateUserOnLogin(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal);
}

public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;
    private readonly AppDbContext _dbContext;
    private readonly IRosettaService _rosettaService;
    private readonly HashSet<string> _developmentAdminIamIds;

    public UserService(ILogger<UserService> logger, AppDbContext dbContext, IConfiguration configuration, IHostEnvironment environment,
        IRosettaService rosettaService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _rosettaService = rosettaService;
        _developmentAdminIamIds = environment.IsDevelopment()
            ? (configuration["DevelopmentData:AdminIamIds"] ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal)
            : [];
    }

    public async Task<bool> IsSiteAdmin(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var iamId = principal.FindFirst("ucdPersonIAMID")?.Value;
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(iamId))
        {
            return false;
        }

        return await _dbContext.Users.AnyAsync(
            user => user.IamId == iamId && user.IsAdmin && user.IsActive, cancellationToken);
    }

    public async Task UpdateUserOnLogin(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("An authenticated identity is required to save the user at login.");
        }

        var iamId = principal.FindFirst("ucdPersonIAMID")?.Value;
        var name = principal.FindFirst("name")?.Value ?? principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(iamId) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("An IAM ID and name are required to save the user at login.");
        }

        var isLocalLogin = principal.Identity.AuthenticationType == LocalAuthentication.Scheme;
        var email = isLocalLogin
            ? principal.FindFirst("preferred_username")?.Value
                ?? principal.FindFirst(ClaimTypes.Email)?.Value
                ?? principal.FindFirst("email")?.Value
            : null;
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var isNewUser = user == null;
        if (user == null)
        {
            // Local sandbox identities are deliberately independent of external directory services.
            if (!isLocalLogin)
            {
                var person = await _rosettaService.FindByIamIdAsync(iamId, cancellationToken);
                if (person != null)
                {
                    name = person.Name;
                    email = person.Email;
                    // TODO: Persist person.Kerberos to Users after its column is approved.
                }
            }

            user = new User
            {
                IamId = iamId,
                Name = name,
                CreatedAt = now,
            };
            _dbContext.Users.Add(user);
        }
        else if (!isLocalLogin)
        {
            // Entra's login address may be a health address. Preserve the saved campus email.
            email = user.Email;
        }

        try
        {
            await SaveLoginDetails(user, name, email, now, cancellationToken);
        }
        catch (DbUpdateException) when (isNewUser)
        {
            // Another first login may have inserted the same IAM ID after our lookup.
            _dbContext.Entry(user).State = EntityState.Detached;
            var existingUser = await _dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
            if (existingUser == null)
            {
                throw;
            }

            await SaveLoginDetails(existingUser, name, isLocalLogin ? email : existingUser.Email,
                DateTimeOffset.UtcNow, cancellationToken);
        }
    }

    private async Task SaveLoginDetails(User user, string name, string? email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        user.Name = name;
        user.Email = email;
        user.UpdatedAt = now;
        user.LastLoginAt = now;
        if (_developmentAdminIamIds.Contains(user.IamId))
        {
            user.IsAdmin = true;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<string>> GetRolesForUser(string userId)
    {
        // fake role strings but use _dbContext to get real roles later
        var roles = new List<string> { "User", "SampleRole" };

        return await Task.FromResult(roles);
    }

    public async Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal)
    {
        // Application roles are authoritative for both sign-in and cookie validation.
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return null; // can't update without user ID
        }

        // get user's roles
        // might want to cache w/ IMemoryCache to avoid DB hits on every request, but we'll skip that for simplicity
        var currentRoles = await GetRolesForUser(userId);

        // compare roles to existing claims, only update if different
        var existingRoles = principal.Identities
            .SelectMany(identity => identity.FindAll(identity.RoleClaimType))
            .Select(claim => claim.Value)
            .ToList();
        var changed = currentRoles.Count != existingRoles.Count ||
                      currentRoles.Except(existingRoles).Any();

        if (!changed) { return null; } // no change

        // Clone each identity to preserve claim mappings and metadata without changing the input.
        var updatedPrincipal = new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));

        foreach (var identity in updatedPrincipal.Identities)
        {
            foreach (var roleClaim in identity.FindAll(identity.RoleClaimType).ToList())
            {
                identity.RemoveClaim(roleClaim);
            }
        }

        var primaryIdentity = (ClaimsIdentity)updatedPrincipal.Identity!;
        foreach (var role in currentRoles)
        {
            primaryIdentity.AddClaim(new Claim(primaryIdentity.RoleClaimType, role));
        }

        return updatedPrincipal;
    }
}

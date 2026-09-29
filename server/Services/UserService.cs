using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Services;

public interface IUserService
{
    Task UpdateUserOnLogin(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<ClaimsPrincipal?> UpdateUserPrincipalIfNeeded(ClaimsPrincipal principal);
}

public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;
    private readonly AppDbContext _dbContext;
    private readonly HashSet<string> _developmentAdminIamIds;

    public UserService(ILogger<UserService> logger, AppDbContext dbContext, IConfiguration configuration, IHostEnvironment environment)
    {
        _logger = logger;
        _dbContext = dbContext;
        _developmentAdminIamIds = environment.IsDevelopment()
            ? (configuration["DevelopmentData:AdminIamIds"] ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal)
            : [];
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

        var email = principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value;
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var isNewUser = user == null;
        if (user == null)
        {
            user = new User
            {
                IamId = iamId,
                Name = name,
                CreatedAt = now,
            };
            _dbContext.Users.Add(user);
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

            await SaveLoginDetails(existingUser, name, email, DateTimeOffset.UtcNow, cancellationToken);
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

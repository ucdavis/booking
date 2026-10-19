using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Models.Teams;

namespace Server.Services;

public sealed class TeamAccessService(AppDbContext dbContext)
{
    public async Task<bool> CanAccessTeam(
        ClaimsPrincipal principal, string? teamSlug, CancellationToken cancellationToken = default)
    {
        var iamId = GetIamId(principal);
        if (iamId == null || string.IsNullOrWhiteSpace(teamSlug))
        {
            return false;
        }

        // Resolve access from current database state so revocation applies to the next request.
        return await dbContext.Users.AnyAsync(user => user.IamId == iamId && user.IsActive &&
            (user.IsAdmin || dbContext.TeamPermissions.Any(permission =>
                permission.UserId == user.Id && permission.Team.Slug == teamSlug &&
                (permission.Role == TeamRole.Admin || permission.Role == TeamRole.Editor || permission.Role == TeamRole.Viewer))),
            cancellationToken);
    }

    public async Task<bool> CanEditTeam(
        ClaimsPrincipal principal, string? teamSlug, CancellationToken cancellationToken = default)
    {
        var iamId = GetIamId(principal);
        if (iamId == null || string.IsNullOrWhiteSpace(teamSlug))
        {
            return false;
        }

        return await dbContext.Users.AnyAsync(user => user.IamId == iamId && user.IsActive &&
            (user.IsAdmin || dbContext.TeamPermissions.Any(permission =>
                permission.UserId == user.Id && permission.Team.Slug == teamSlug &&
                (permission.Role == TeamRole.Admin || permission.Role == TeamRole.Editor))), cancellationToken);
    }

    public async Task<bool> CanAdministerTeam(
        ClaimsPrincipal principal, string? teamSlug, CancellationToken cancellationToken = default)
    {
        var iamId = GetIamId(principal);
        if (iamId == null || string.IsNullOrWhiteSpace(teamSlug))
        {
            return false;
        }

        return await dbContext.Users.AnyAsync(user => user.IamId == iamId && user.IsActive &&
            (user.IsAdmin || dbContext.TeamPermissions.Any(permission =>
                permission.UserId == user.Id && permission.Team.Slug == teamSlug && permission.Role == TeamRole.Admin)),
            cancellationToken);
    }

    public async Task<List<TeamSummaryResponse>> GetMemberships(
        ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var iamId = GetIamId(principal);
        if (iamId == null)
        {
            return [];
        }

        // Site administrators use the all-teams list; this menu contains explicit memberships only.
        var permissions = await dbContext.TeamPermissions.AsNoTracking()
            .Where(permission => permission.User.IamId == iamId && permission.User.IsActive &&
                (permission.Role == TeamRole.Admin || permission.Role == TeamRole.Editor || permission.Role == TeamRole.Viewer))
            .OrderBy(permission => permission.Team.Name)
            .ThenBy(permission => permission.Team.Slug)
            .Select(permission => new
            {
                Id = permission.Team.Id,
                Name = permission.Team.Name,
                Slug = permission.Team.Slug,
                permission.Role,
            })
            .ToListAsync(cancellationToken);

        return permissions.GroupBy(permission => new { permission.Id, permission.Name, permission.Slug })
            .Select(group => new TeamSummaryResponse
            {
                Id = group.Key.Id,
                Name = group.Key.Name,
                Slug = group.Key.Slug,
                Roles = group.Select(permission => permission.Role).OrderBy(role => role).ToList(),
            })
            .ToList();
    }

    public async Task<TeamAccessResponse?> GetAccess(
        ClaimsPrincipal principal, string teamSlug, CancellationToken cancellationToken = default)
    {
        var iamId = GetIamId(principal);
        if (iamId == null || string.IsNullOrWhiteSpace(teamSlug))
        {
            return null;
        }

        var user = await dbContext.Users.AsNoTracking()
            .Where(user => user.IamId == iamId && user.IsActive)
            .Select(user => new { user.Id, user.IsAdmin })
            .SingleOrDefaultAsync(cancellationToken);
        if (user == null)
        {
            return null;
        }

        var team = await dbContext.Teams.AsNoTracking()
            .Where(team => team.Slug == teamSlug)
            .Select(team => new { team.Id, team.Name, team.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        if (team == null)
        {
            return null;
        }

        var roles = await dbContext.TeamPermissions.AsNoTracking()
            .Where(permission => permission.TeamId == team.Id && permission.UserId == user.Id &&
                (permission.Role == TeamRole.Admin || permission.Role == TeamRole.Editor || permission.Role == TeamRole.Viewer))
            .OrderBy(permission => permission.Role)
            .Select(permission => permission.Role)
            .ToListAsync(cancellationToken);
        if (!user.IsAdmin && roles.Count == 0)
        {
            return null;
        }

        return new TeamAccessResponse
        {
            Team = new TeamSummaryResponse { Id = team.Id, Name = team.Name, Slug = team.Slug, Roles = roles },
            Roles = roles,
            IsSiteAdmin = user.IsAdmin,
        };
    }

    private static string? GetIamId(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var iamId = principal.FindFirst("ucdPersonIAMID")?.Value;
        return string.IsNullOrWhiteSpace(iamId) ? null : iamId;
    }
}

using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Teams;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/teams/{teamSlug}/members")]
[Authorize(Policy = AuthenticationHelper.TeamAdminPolicy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeamMembersController(AppDbContext dbContext, IRosettaService rosettaService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TeamMemberResponse>>> GetMembers(
        string teamSlug, CancellationToken cancellationToken = default)
    {
        var teamId = await FindTeamId(teamSlug, cancellationToken);
        if (teamId == null)
        {
            return NotFound("That team could not be found.");
        }

        var permissions = await dbContext.TeamPermissions.AsNoTracking()
            .Where(permission => permission.TeamId == teamId)
            .OrderBy(permission => permission.User.Name)
            .ThenBy(permission => permission.User.IamId)
            .Select(permission => new
            {
                Id = permission.UserId,
                IamId = permission.User.IamId,
                Name = permission.User.Name,
                Email = permission.User.Email,
                IsActive = permission.User.IsActive,
                permission.Role,
            })
            .ToListAsync(cancellationToken);

        var members = permissions.GroupBy(permission => new
            {
                permission.Id, permission.IamId, permission.Name, permission.Email, permission.IsActive,
            })
            .Select(group => new TeamMemberResponse
            {
                Id = group.Key.Id,
                IamId = group.Key.IamId,
                Name = group.Key.Name,
                Email = group.Key.Email,
                IsActive = group.Key.IsActive,
                Roles = group.Select(permission => permission.Role).OrderBy(role => role).ToList(),
            }).ToList();

        return Ok(members);
    }

    [HttpGet("people")]
    public async Task<ActionResult<List<TeamPersonResponse>>> SearchPeople(
        string teamSlug, [FromQuery] string? query, CancellationToken cancellationToken = default)
    {
        var search = query?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length > 128)
        {
            return BadRequest("Enter an email, IAM ID, or Kerb of no more than 128 characters.");
        }

        var teamId = await FindTeamId(teamSlug, cancellationToken);
        if (teamId == null)
        {
            return NotFound("That team could not be found.");
        }

        var people = (await rosettaService.SearchPeopleAsync(search, cancellationToken))
            .Where(person => person.HasRequiredDetails()).Take(10).ToList();
        var iamIds = people.Select(person => person.IamId).ToList();
        var userPermissions = await (
            from user in dbContext.Users.AsNoTracking()
            where iamIds.Contains(user.IamId)
            join permission in dbContext.TeamPermissions.AsNoTracking().Where(permission => permission.TeamId == teamId)
                on user.Id equals permission.UserId into permissions
            from permission in permissions.DefaultIfEmpty()
            select new
            {
                user.IamId,
                user.IsActive,
                Role = permission == null ? null : (TeamRole?)permission.Role,
            })
            .ToListAsync(cancellationToken);
        var users = userPermissions.GroupBy(user => user.IamId)
            .ToDictionary(group => group.Key, group => new
            {
                IsActive = group.First().IsActive,
                Roles = group.Where(user => user.Role != null).Select(user => user.Role!.Value)
                    .OrderBy(role => role).ToList(),
            });
        var matches = people.Select(person =>
        {
            users.TryGetValue(person.IamId, out var user);
            return new TeamPersonResponse
            {
                IamId = person.IamId,
                Name = person.Name,
                Email = person.Email,
                Kerberos = person.Kerberos,
                IsActive = user == null || user.IsActive,
                Roles = user?.Roles ?? [],
            };
        }).ToList();

        return Ok(matches);
    }

    [HttpPost]
    public async Task<ActionResult<TeamMemberResponse>> AddMember(
        string teamSlug, [FromBody] AddTeamMemberRequest request, CancellationToken cancellationToken = default)
    {
        var iamId = request.IamId?.Trim();
        if (string.IsNullOrEmpty(iamId) || iamId.Length > 10)
        {
            return BadRequest("A valid IAM ID is required.");
        }
        if (!HasValidRoles(request.Roles) || request.Roles!.Any(role => !CanAssignRole(role)))
        {
            return BadRequest("Choose at least one Admin, Editor, or Approver role without duplicates.");
        }
        var roles = request.Roles!.OrderBy(role => role).ToList();

        var teamId = await FindTeamId(teamSlug, cancellationToken);
        if (teamId == null)
        {
            return NotFound("That team could not be found.");
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var isNewUser = user == null;
        if (user != null && !user.IsActive)
        {
            return Conflict("This user is inactive and cannot be added to the team.");
        }
        if (user != null && await HasMembership(teamId.Value, user.Id, cancellationToken))
        {
            return Conflict("This user already belongs to the team. Change their roles from the member list.");
        }

        // Recheck required directory details before granting access to an existing or new user.
        var person = await rosettaService.FindByIamIdAsync(iamId, cancellationToken);
        if (person == null || !person.HasRequiredDetails() || person.IamId != iamId)
        {
            return NotFound("That person could not be found. Search again before adding a team member.");
        }

        // The role is part of the key, so a transaction protects the whole membership from concurrent adds.
        await using var transaction = await BeginMembershipTransaction(cancellationToken);
        if (user != null && await HasMembership(teamId.Value, user.Id, cancellationToken))
        {
            return Conflict("This user already belongs to the team. Change their roles from the member list.");
        }
        if (user == null)
        {
            var now = DateTimeOffset.UtcNow;
            user = new User
            {
                IamId = iamId,
                Name = person.Name,
                Email = person.Email,
                Kerberos = person.Kerberos,
                CreatedAt = now,
                UpdatedAt = now,
            };
            dbContext.Users.Add(user);
        }

        PopulateMissingDetails(user, person.Email!, person.Kerberos!);
        var permissions = roles.Select(role => new TeamPermission { TeamId = teamId.Value, User = user, Role = role })
            .ToList();
        dbContext.TeamPermissions.AddRange(permissions);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException)
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }

            // A first login or another add may have won a unique key. Discard the attempted inserts.
            foreach (var permission in permissions)
            {
                dbContext.Entry(permission).State = EntityState.Detached;
            }
            if (isNewUser)
            {
                dbContext.Entry(user).State = EntityState.Detached;
            }

            await using var retryTransaction = await BeginMembershipTransaction(cancellationToken);
            user = await dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
            if (user == null)
            {
                throw;
            }
            if (!user.IsActive)
            {
                return Conflict("This user is inactive and cannot be added to the team.");
            }
            if (await HasMembership(teamId.Value, user.Id, cancellationToken))
            {
                return Conflict("This user already belongs to the team. Refresh the member list.");
            }
            if (!isNewUser)
            {
                throw;
            }

            PopulateMissingDetails(user, person.Email!, person.Kerberos!);
            permissions = roles.Select(role => new TeamPermission { TeamId = teamId.Value, User = user, Role = role })
                .ToList();
            dbContext.TeamPermissions.AddRange(permissions);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (retryTransaction != null)
                {
                    await retryTransaction.CommitAsync(cancellationToken);
                }
            }
            catch (DbUpdateException)
            {
                if (retryTransaction != null)
                {
                    await retryTransaction.DisposeAsync();
                }

                foreach (var permission in permissions)
                {
                    dbContext.Entry(permission).State = EntityState.Detached;
                }
                if (await HasMembership(teamId.Value, user.Id, cancellationToken))
                {
                    return Conflict("This user already belongs to the team. Refresh the member list.");
                }

                throw;
            }
        }

        return Ok(ToResponse(user, roles));
    }

    [HttpPut("{userId:int}/roles")]
    public async Task<ActionResult<TeamMemberResponse>> UpdateRoles(
        string teamSlug, int userId, [FromBody] UpdateTeamMemberRolesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidRoles(request.Roles))
        {
            return BadRequest("Choose at least one valid team role without duplicates.");
        }
        var roles = request.Roles!.OrderBy(role => role).ToList();

        var currentIamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (string.IsNullOrWhiteSpace(currentIamId))
        {
            return Forbid();
        }

        await using var transaction = await BeginMembershipTransaction(cancellationToken);
        var permissions = await dbContext.TeamPermissions.Include(permission => permission.User)
            .Where(permission => permission.Team.Slug == teamSlug && permission.UserId == userId)
            .ToListAsync(cancellationToken);
        if (permissions.Count == 0)
        {
            return NotFound("That team member could not be found.");
        }
        var user = permissions[0].User;
        var existingRoles = permissions.Select(permission => permission.Role).ToList();
        var isSelf = string.Equals(user.IamId.Trim(), currentIamId.Trim(), StringComparison.OrdinalIgnoreCase);
        if (isSelf && existingRoles.Contains(TeamRole.Admin) && !roles.Contains(TeamRole.Admin) &&
            !request.ConfirmSelfAccessChange)
        {
            return BadRequest("Confirm removing your own Admin role. You may lose access to manage this team's members.");
        }

        if (roles.Any(role => !CanAssignRole(role) && !existingRoles.Contains(role)))
        {
            return BadRequest("New role assignments must use Admin, Editor, or Approver.");
        }

        dbContext.TeamPermissions.RemoveRange(permissions.Where(permission => !roles.Contains(permission.Role)));
        dbContext.TeamPermissions.AddRange(roles.Where(role => !existingRoles.Contains(role))
            .Select(role => new TeamPermission { TeamId = permissions[0].TeamId, User = user, Role = role }));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }

            if (exception is DbUpdateConcurrencyException ||
                await MembershipChanged(permissions[0].TeamId, userId, existingRoles, cancellationToken))
            {
                return Conflict("This member's roles changed while saving. Refresh the member list and try again.");
            }

            throw;
        }
        return Ok(ToResponse(user, roles));
    }

    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> RemoveMember(
        string teamSlug, int userId, [FromQuery] bool confirmSelfAccessChange = false,
        CancellationToken cancellationToken = default)
    {
        var currentIamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (string.IsNullOrWhiteSpace(currentIamId))
        {
            return Forbid();
        }

        await using var transaction = await BeginMembershipTransaction(cancellationToken);
        var permissions = await dbContext.TeamPermissions.Include(permission => permission.User)
            .Where(permission => permission.Team.Slug == teamSlug && permission.UserId == userId)
            .ToListAsync(cancellationToken);
        if (permissions.Count == 0)
        {
            return NotFound("That team member could not be found.");
        }
        if (string.Equals(permissions[0].User.IamId.Trim(), currentIamId.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !confirmSelfAccessChange)
        {
            return BadRequest("Confirm removing your own team membership. You may lose access to this team.");
        }

        dbContext.TeamPermissions.RemoveRange(permissions);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }

            if (exception is DbUpdateConcurrencyException ||
                await MembershipChanged(permissions[0].TeamId, userId,
                    permissions.Select(permission => permission.Role).ToList(), cancellationToken))
            {
                return Conflict("This member's roles changed while removing them. Refresh the member list and try again.");
            }

            throw;
        }
        return NoContent();
    }

    private Task<int?> FindTeamId(string teamSlug, CancellationToken cancellationToken)
        => dbContext.Teams.Where(team => team.Slug == teamSlug).Select(team => (int?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<bool> HasMembership(int teamId, int userId, CancellationToken cancellationToken)
        => dbContext.TeamPermissions.AnyAsync(permission => permission.TeamId == teamId && permission.UserId == userId,
            cancellationToken);

    private async Task<IDbContextTransaction?> BeginMembershipTransaction(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private async Task<bool> MembershipChanged(
        int teamId, int userId, List<TeamRole> originalRoles, CancellationToken cancellationToken)
    {
        var currentRoles = await dbContext.TeamPermissions.AsNoTracking()
            .Where(permission => permission.TeamId == teamId && permission.UserId == userId)
            .Select(permission => permission.Role).ToListAsync(cancellationToken);
        return !originalRoles.OrderBy(role => role).SequenceEqual(currentRoles.OrderBy(role => role));
    }

    private static bool CanAssignRole(TeamRole? role)
        => role == TeamRole.Admin || role == TeamRole.Editor || role == TeamRole.Approver;

    private static bool HasValidRoles(List<TeamRole>? roles)
        => roles != null && roles.Count > 0 && roles.Distinct().Count() == roles.Count &&
            roles.All(role => CanAssignRole(role) || role == TeamRole.Viewer);

    private static void PopulateMissingDetails(User user, string email, string kerberos)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            user.Email = email;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }
        if (string.IsNullOrWhiteSpace(user.Kerberos))
        {
            user.Kerberos = kerberos;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static TeamMemberResponse ToResponse(User user, List<TeamRole> roles) => new()
    {
        Id = user.Id,
        IamId = user.IamId,
        Name = user.Name,
        Email = user.Email,
        IsActive = user.IsActive,
        Roles = roles,
    };
}

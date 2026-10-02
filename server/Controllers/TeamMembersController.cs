using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Teams;

namespace Server.Controllers;

[ApiController]
[Route("api/teams/{teamSlug}/members")]
[Authorize(Policy = AuthenticationHelper.TeamAdminPolicy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeamMembersController(AppDbContext dbContext) : ControllerBase
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

        var members = await dbContext.TeamPermissions.AsNoTracking()
            .Where(permission => permission.TeamId == teamId)
            .OrderBy(permission => permission.User.Name)
            .ThenBy(permission => permission.User.IamId)
            .Select(permission => new TeamMemberResponse
            {
                Id = permission.UserId,
                IamId = permission.User.IamId,
                Name = permission.User.Name,
                Email = permission.User.Email,
                IsActive = permission.User.IsActive,
                Role = permission.Role,
            })
            .ToListAsync(cancellationToken);

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

        var matches = await (
            from person in dbContext.People.AsNoTracking()
            where person.Email == search || person.IamId == search || person.UserId == search
            join user in dbContext.Users.AsNoTracking() on person.IamId equals user.IamId into users
            from user in users.DefaultIfEmpty()
            join permission in dbContext.TeamPermissions.AsNoTracking().Where(permission => permission.TeamId == teamId)
                on (user == null ? (int?)null : user.Id) equals (int?)permission.UserId into permissions
            from permission in permissions.DefaultIfEmpty()
            orderby person.FullName, person.IamId
            select new TeamPersonResponse
            {
                IamId = person.IamId.Trim(),
                Name = (person.FullName ?? "").Trim() != ""
                    ? person.FullName!.Trim()
                    : ((person.FirstName ?? "") + " " + (person.LastName ?? "")).Trim(),
                Email = person.Email == null ? null : person.Email!.Trim(),
                Kerberos = person.UserId == null ? null : person.UserId!.Trim(),
                IsActive = user == null || user.IsActive,
                IsActiveInIam = person.IsActiveInIam,
                Role = permission == null ? null : (TeamRole?)permission.Role,
            })
            .Take(10)
            .ToListAsync(cancellationToken);

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
        if (!CanAssignRole(request.Role))
        {
            return BadRequest("Choose the Admin or Editor role.");
        }

        var teamId = await FindTeamId(teamSlug, cancellationToken);
        if (teamId == null)
        {
            return NotFound("That team could not be found.");
        }

        // Resolve the directory record again; profile fields and IAM status are never trusted from the client.
        var person = await dbContext.People.AsNoTracking()
            .Where(person => person.IamId == iamId)
            .Select(person => new
            {
                person.IamId, person.FullName, person.FirstName, person.LastName, person.Email, person.IsActiveInIam,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (person == null)
        {
            return NotFound("That person could not be found. Search again before adding a team member.");
        }
        if (!person.IsActiveInIam)
        {
            return Conflict("This person is inactive in IAM and cannot be added to the team.");
        }

        iamId = person.IamId.Trim();
        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var isNewUser = user == null;
        if (user != null && !user.IsActive)
        {
            return Conflict("This user is inactive and cannot be added to the team.");
        }
        if (user != null && await HasMembership(teamId.Value, user.Id, cancellationToken))
        {
            return Conflict("This user already belongs to the team. Change their role from the member list.");
        }

        if (user == null)
        {
            var name = string.IsNullOrWhiteSpace(person.FullName)
                ? $"{person.FirstName} {person.LastName}".Trim()
                : person.FullName.Trim();
            var now = DateTimeOffset.UtcNow;
            user = new User
            {
                IamId = iamId,
                Name = string.IsNullOrWhiteSpace(name) ? iamId : name,
                Email = person.Email?.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            dbContext.Users.Add(user);
        }

        var permission = new TeamPermission { TeamId = teamId.Value, User = user, Role = request.Role!.Value };
        dbContext.TeamPermissions.Add(permission);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A first login or another add may have won either unique key. Discard both attempted inserts.
            dbContext.Entry(permission).State = EntityState.Detached;
            if (isNewUser)
            {
                dbContext.Entry(user).State = EntityState.Detached;
            }

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

            permission = new TeamPermission { TeamId = teamId.Value, User = user, Role = request.Role.Value };
            dbContext.TeamPermissions.Add(permission);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(permission).State = EntityState.Detached;
                if (await HasMembership(teamId.Value, user.Id, cancellationToken))
                {
                    return Conflict("This user already belongs to the team. Refresh the member list.");
                }

                throw;
            }
        }

        return Ok(ToResponse(user, permission.Role));
    }

    [HttpPut("{userId:int}/role")]
    public async Task<ActionResult<TeamMemberResponse>> UpdateRole(
        string teamSlug, int userId, [FromBody] UpdateTeamMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!CanAssignRole(request.Role))
        {
            return BadRequest("Choose the Admin or Editor role.");
        }

        var currentIamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (string.IsNullOrWhiteSpace(currentIamId))
        {
            return Forbid();
        }

        var permission = await dbContext.TeamPermissions.Include(permission => permission.User)
            .SingleOrDefaultAsync(permission => permission.Team.Slug == teamSlug && permission.UserId == userId,
                cancellationToken);
        if (permission == null)
        {
            return NotFound("That team member could not be found.");
        }
        if (string.Equals(permission.User.IamId.Trim(), currentIamId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("You cannot change your own team role.");
        }

        permission.Role = request.Role!.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(permission.User, permission.Role));
    }

    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> RemoveMember(
        string teamSlug, int userId, CancellationToken cancellationToken = default)
    {
        var currentIamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (string.IsNullOrWhiteSpace(currentIamId))
        {
            return Forbid();
        }

        var permission = await dbContext.TeamPermissions.Include(permission => permission.User)
            .SingleOrDefaultAsync(permission => permission.Team.Slug == teamSlug && permission.UserId == userId,
                cancellationToken);
        if (permission == null)
        {
            return NotFound("That team member could not be found.");
        }
        if (string.Equals(permission.User.IamId.Trim(), currentIamId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("You cannot remove your own team access.");
        }

        dbContext.TeamPermissions.Remove(permission);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<int?> FindTeamId(string teamSlug, CancellationToken cancellationToken)
        => dbContext.Teams.Where(team => team.Slug == teamSlug).Select(team => (int?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<bool> HasMembership(int teamId, int userId, CancellationToken cancellationToken)
        => dbContext.TeamPermissions.AnyAsync(permission => permission.TeamId == teamId && permission.UserId == userId,
            cancellationToken);

    private static bool CanAssignRole(TeamRole? role) => role == TeamRole.Admin || role == TeamRole.Editor;

    private static TeamMemberResponse ToResponse(User user, TeamRole role) => new()
    {
        Id = user.Id,
        IamId = user.IamId,
        Name = user.Name,
        Email = user.Email,
        IsActive = user.IsActive,
        Role = role,
    };
}

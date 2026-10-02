using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        var people = (await rosettaService.SearchPeopleAsync(search, cancellationToken)).Take(10).ToList();
        var iamIds = people.Select(person => person.IamId).ToList();
        var users = await (
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
            .ToDictionaryAsync(user => user.IamId, cancellationToken);
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
                IsActiveInIam = person.IsActiveInIam,
                Role = user?.Role,
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
        if (!CanAssignRole(request.Role))
        {
            return BadRequest("Choose the Admin or Editor role.");
        }

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
            return Conflict("This user already belongs to the team. Change their role from the member list.");
        }

        // Recheck IAM before granting access, including when the search matched an existing user.
        var person = await rosettaService.FindByIamIdAsync(iamId, cancellationToken);
        if (person == null)
        {
            return NotFound("That person could not be found. Search again before adding a team member.");
        }
        if (person.IsActiveInIam != true)
        {
            return Conflict(person.IsActiveInIam == false
                ? "This person is inactive in IAM and cannot be added to the team."
                : "This person's IAM activity could not be verified. Search again before adding a team member.");
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

        PopulateKerberosIfMissing(user, person.Kerberos);
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

            PopulateKerberosIfMissing(user, person.Kerberos);
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

    private static void PopulateKerberosIfMissing(User user, string? kerberos)
    {
        if (string.IsNullOrWhiteSpace(user.Kerberos) && !string.IsNullOrWhiteSpace(kerberos))
        {
            user.Kerberos = kerberos;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

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

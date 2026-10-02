using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Admin;
using Server.Models.Teams;
using Server.Services;

namespace Server.Controllers;

[Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminController(AppDbContext dbContext, IRosettaService rosettaService) : ApiControllerBase
{
    [HttpGet("access")]
    public IActionResult Access() => NoContent();

    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamSummaryResponse>>> GetTeams(CancellationToken cancellationToken = default)
    {
        var teams = await dbContext.Teams.AsNoTracking()
            .OrderBy(team => team.Name)
            .ThenBy(team => team.Slug)
            .Select(team => new TeamSummaryResponse { Id = team.Id, Name = team.Name, Slug = team.Slug })
            .ToListAsync(cancellationToken);

        return Ok(teams);
    }

    [HttpPost("teams")]
    public async Task<ActionResult<TeamSummaryResponse>> CreateTeam(
        [FromBody] CreateTeamRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        var slug = request.Slug?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return BadRequest("Enter a team name of no more than 200 characters.");
        }

        if (string.IsNullOrEmpty(slug) || slug.Length > 100 ||
            !Regex.IsMatch(slug, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant))
        {
            return BadRequest("Enter a slug of no more than 100 lowercase letters, numbers, and single hyphens between words.");
        }

        if (await dbContext.Teams.AnyAsync(team => team.Slug == slug, cancellationToken))
        {
            return Conflict("That team slug is already in use. Choose another slug.");
        }

        var now = DateTimeOffset.UtcNow;
        var team = new Team { Name = name, Slug = slug, CreatedAt = now, UpdatedAt = now };
        dbContext.Teams.Add(team);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(team).State = EntityState.Detached;
            if (await dbContext.Teams.AnyAsync(existing => existing.Slug == slug, cancellationToken))
            {
                return Conflict("That team slug is already in use. Choose another slug.");
            }

            throw;
        }

        return CreatedAtAction(nameof(TeamsController.GetTeam), "Teams", new { teamSlug = team.Slug },
            new TeamSummaryResponse { Id = team.Id, Name = team.Name, Slug = team.Slug });
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserResponse>>> GetUsers(CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users.AsNoTracking()
            .Where(user => user.IsAdmin)
            .OrderBy(user => user.Name)
            .ThenBy(user => user.IamId)
            .Select(user => new AdminUserResponse
            {
                Id = user.Id,
                IamId = user.IamId,
                Name = user.Name,
                Email = user.Email,
                IsActive = user.IsActive,
            })
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("people")]
    public async Task<ActionResult<List<AdminPersonResponse>>> SearchPeople(
        [FromQuery] string? query, CancellationToken cancellationToken = default)
    {
        var search = query?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length > 128)
        {
            return BadRequest("Enter an email, IAM ID, or Kerb of no more than 128 characters.");
        }

        var people = (await rosettaService.SearchPeopleAsync(search, cancellationToken)).Take(10).ToList();
        var iamIds = people.Select(person => person.IamId).ToList();
        var users = await dbContext.Users.AsNoTracking()
            .Where(user => iamIds.Contains(user.IamId))
            .ToDictionaryAsync(user => user.IamId, cancellationToken);
        var matches = people.Select(person =>
        {
            users.TryGetValue(person.IamId, out var user);
            return new AdminPersonResponse
            {
                IamId = person.IamId,
                Name = person.Name,
                Email = person.Email,
                Kerberos = person.Kerberos,
                IsAdmin = user != null && user.IsAdmin,
                IsActive = user == null || user.IsActive,
                IsActiveInIam = person.IsActiveInIam,
            };
        }).ToList();

        return Ok(matches);
    }

    [HttpPost("users")]
    public async Task<ActionResult<AdminUserResponse>> AddUser(
        [FromBody] AddAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        var iamId = request.IamId?.Trim();
        if (string.IsNullOrEmpty(iamId) || iamId.Length > 10)
        {
            return BadRequest("A valid IAM ID is required.");
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var isNewUser = user == null;
        if (user != null && !user.IsActive)
        {
            return Conflict("This user is inactive and cannot be added as a site admin.");
        }
        if (user?.IsAdmin == true)
        {
            return Ok(ToResponse(user));
        }

        // Recheck IAM before granting access, including when the search matched an existing user.
        var person = await rosettaService.FindByIamIdAsync(iamId, cancellationToken);
        if (person == null)
        {
            return NotFound("That person could not be found. Search again before adding an admin.");
        }

        if (person.IsActiveInIam != true)
        {
            return Conflict(person.IsActiveInIam == false
                ? "This person is inactive in IAM and cannot be added as a site admin."
                : "This person's IAM activity could not be verified. Search again before adding a site admin.");
        }

        if (user == null)
        {
            user = new User
            {
                IamId = iamId,
                Name = person.Name,
                Email = person.Email,
                Kerberos = person.Kerberos,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.Users.Add(user);
        }

        try
        {
            await GrantAdmin(user, person.Kerberos, cancellationToken);
        }
        catch (DbUpdateException) when (isNewUser)
        {
            // A concurrent first login or admin addition may have inserted this IAM ID.
            dbContext.Entry(user).State = EntityState.Detached;
            user = await dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
            if (user == null)
            {
                throw;
            }

            if (!user.IsActive)
            {
                return Conflict("This user is inactive and cannot be added as a site admin.");
            }

            await GrantAdmin(user, person.Kerberos, cancellationToken);
        }

        return Ok(ToResponse(user));
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> RemoveUser(int id, CancellationToken cancellationToken = default)
    {
        var currentIamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (string.IsNullOrWhiteSpace(currentIamId))
        {
            return Forbid();
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound("That user could not be found.");
        }

        if (string.Equals(user.IamId.Trim(), currentIamId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("You cannot remove your own site admin access.");
        }

        if (user.IsAdmin)
        {
            user.IsAdmin = false;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task GrantAdmin(User user, string? kerberos, CancellationToken cancellationToken)
    {
        var populateKerberos = string.IsNullOrWhiteSpace(user.Kerberos) && !string.IsNullOrWhiteSpace(kerberos);
        if (user.IsAdmin && !populateKerberos)
        {
            return;
        }

        if (populateKerberos)
        {
            user.Kerberos = kerberos;
        }
        user.IsAdmin = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AdminUserResponse ToResponse(User user) => new()
    {
        Id = user.Id,
        IamId = user.IamId,
        Name = user.Name,
        Email = user.Email,
        IsActive = user.IsActive,
    };
}

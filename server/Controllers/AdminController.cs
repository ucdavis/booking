using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Admin;
using Server.Models.Teams;

namespace Server.Controllers;

[Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminController(AppDbContext dbContext) : ApiControllerBase
{
    [HttpGet("access")]
    public IActionResult Access() => NoContent();

    [HttpGet("antiforgery")]
    public IActionResult AntiforgeryToken([FromServices] IAntiforgery antiforgery)
        => Ok(new { Token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

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
    [ValidateAntiForgeryToken]
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

        // Keep the filter, projection, and result limit in SQL for the large directory.
        var matches = await (
            from person in dbContext.People.AsNoTracking()
            where person.Email == search || person.IamId == search || person.UserId == search
            join user in dbContext.Users.AsNoTracking() on person.IamId equals user.IamId into users
            from user in users.DefaultIfEmpty()
            orderby person.FullName, person.IamId
            select new AdminPersonResponse
            {
                IamId = person.IamId.Trim(),
                Name = (person.FullName ?? "").Trim() != ""
                    ? person.FullName!.Trim()
                    : ((person.FirstName ?? "") + " " + (person.LastName ?? "")).Trim(),
                Email = person.Email == null ? null : person.Email.Trim(),
                Kerberos = person.UserId == null ? null : person.UserId.Trim(),
                IsAdmin = user != null && user.IsAdmin,
                IsActive = user == null || user.IsActive,
                IsActiveInIam = person.IsActiveInIam,
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        return Ok(matches);
    }

    [HttpPost("users")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<AdminUserResponse>> AddUser(
        [FromBody] AddAdminUserRequest request, CancellationToken cancellationToken = default)
    {
        var iamId = request.IamId?.Trim();
        if (string.IsNullOrEmpty(iamId) || iamId.Length > 10)
        {
            return BadRequest("A valid IAM ID is required.");
        }

        // Resolve the selected person again; names, email, and privileges never come from the client.
        var person = await dbContext.People.AsNoTracking()
            .Where(person => person.IamId == iamId)
            .Select(person => new
            {
                person.IamId, person.FullName, person.FirstName, person.LastName, person.Email, person.IsActiveInIam,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (person == null)
        {
            return NotFound("That person could not be found. Search again before adding an admin.");
        }

        if (!person.IsActiveInIam)
        {
            return Conflict("This person is inactive in IAM and cannot be added as a site admin.");
        }

        iamId = person.IamId.Trim();
        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.IamId == iamId, cancellationToken);
        var isNewUser = user == null;
        if (user != null && !user.IsActive)
        {
            return Conflict("This user is inactive and cannot be added as a site admin.");
        }

        if (user == null)
        {
            var name = string.IsNullOrWhiteSpace(person.FullName)
                ? $"{person.FirstName} {person.LastName}".Trim()
                : person.FullName.Trim();
            user = new User
            {
                IamId = iamId,
                Name = string.IsNullOrWhiteSpace(name) ? iamId : name,
                Email = person.Email?.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.Users.Add(user);
        }

        try
        {
            await GrantAdmin(user, cancellationToken);
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

            await GrantAdmin(user, cancellationToken);
        }

        return Ok(ToResponse(user));
    }

    [HttpDelete("users/{id:int}")]
    [ValidateAntiForgeryToken]
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

    private async Task GrantAdmin(User user, CancellationToken cancellationToken)
    {
        if (user.IsAdmin)
        {
            return;
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

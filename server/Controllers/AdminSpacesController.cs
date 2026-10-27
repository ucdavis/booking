using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Spaces;
using Server.Models.Teams;

namespace Server.Controllers;

[ApiController]
[Route("api/admin/spaces")]
[Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSpacesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SpaceResponse>>> GetSpaces(CancellationToken cancellationToken = default)
    {
        var spaces = await dbContext.Spaces.AsNoTracking()
            .OrderBy(space => space.Name)
            .ThenBy(space => space.Slug)
            .Select(space => new SpaceResponse
            {
                Id = space.Id, Name = space.Name, Slug = space.Slug,
                Description = space.Description, Address = space.Address,
                IsOfficialFacility = space.IsOfficialFacility, IsActive = space.IsActive,
                ReferenceNumber = space.ReferenceNumber,
            })
            .ToListAsync(cancellationToken);

        return Ok(spaces);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SpaceResponse>> GetSpace(int id, CancellationToken cancellationToken = default)
    {
        var space = await dbContext.Spaces.AsNoTracking()
            .Where(space => space.Id == id)
            .Select(space => new SpaceResponse
            {
                Id = space.Id, Name = space.Name, Slug = space.Slug,
                Description = space.Description, Address = space.Address,
                IsOfficialFacility = space.IsOfficialFacility, IsActive = space.IsActive,
                ReferenceNumber = space.ReferenceNumber,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return space == null ? NotFound("That space could not be found.") : Ok(space);
    }

    [HttpPost]
    public async Task<ActionResult<SpaceResponse>> CreateSpace(
        [FromBody] SaveSpaceRequest request, CancellationToken cancellationToken = default)
    {
        var error = ValidateRequest(request, out var name, out var slug);
        if (error != null)
        {
            return BadRequest(error);
        }
        if (await SlugExists(slug, null, cancellationToken))
        {
            return DuplicateConflict("duplicate_slug");
        }
        if (request.IsOfficialFacility &&
            await OfficialReferenceExists(TrimOptional(request.ReferenceNumber), null, cancellationToken))
        {
            return DuplicateConflict("duplicate_reference_number");
        }

        var now = DateTimeOffset.UtcNow;
        var space = new Space
        {
            Name = name,
            Slug = slug,
            TimeZoneId = "America/Los_Angeles",
            CreatedAt = now,
            UpdatedAt = now,
        };
        ApplyEditableFields(space, request, name);
        dbContext.Spaces.Add(space);
        var conflict = await SaveSpace(space, cancellationToken);
        if (conflict != null)
        {
            return DuplicateConflict(conflict);
        }

        return CreatedAtAction(nameof(GetSpace), new { id = space.Id }, ToResponse(space));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SpaceResponse>> UpdateSpace(
        int id, [FromBody] SaveSpaceRequest request, CancellationToken cancellationToken = default)
    {
        var error = ValidateRequest(request, out var name, out _);
        if (error != null)
        {
            return BadRequest(error);
        }

        var space = await dbContext.Spaces.SingleOrDefaultAsync(space => space.Id == id, cancellationToken);
        if (space == null)
        {
            return NotFound("That space could not be found.");
        }
        if (request.Slug != space.Slug)
        {
            return BadRequest("A space's slug cannot be changed after it is created.");
        }
        if (request.IsOfficialFacility &&
            await OfficialReferenceExists(TrimOptional(request.ReferenceNumber), id, cancellationToken))
        {
            return DuplicateConflict("duplicate_reference_number");
        }

        ApplyEditableFields(space, request, name);
        space.UpdatedAt = DateTimeOffset.UtcNow;
        var conflict = await SaveSpace(space, cancellationToken);
        if (conflict != null)
        {
            return DuplicateConflict(conflict);
        }

        return Ok(ToResponse(space));
    }

    [HttpGet("{id:int}/teams")]
    public async Task<ActionResult<List<SpaceTeamAccessResponse>>> GetTeams(int id, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Spaces.AnyAsync(space => space.Id == id, cancellationToken))
        {
            return NotFound("That space could not be found.");
        }

        var teams = await dbContext.TeamSpaces.AsNoTracking()
            .Where(link => link.SpaceId == id)
            .OrderBy(link => link.Team.Name)
            .ThenBy(link => link.Team.Slug)
            .Select(link => new SpaceTeamAccessResponse
            {
                Team = new TeamSummaryResponse { Id = link.TeamId, Name = link.Team.Name, Slug = link.Team.Slug },
                ResourceCount = dbContext.Resources.Count(resource => resource.TeamId == link.TeamId && resource.SpaceId == id),
            })
            .ToListAsync(cancellationToken);

        return Ok(teams);
    }

    [HttpPut("{id:int}/teams/{teamId:int}")]
    public async Task<ActionResult<SpaceTeamAccessResponse>> AssignTeam(
        int id, int teamId, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Spaces.AnyAsync(space => space.Id == id, cancellationToken))
        {
            return NotFound("That space could not be found.");
        }
        var team = await dbContext.Teams.AsNoTracking()
            .Where(team => team.Id == teamId)
            .Select(team => new TeamSummaryResponse { Id = team.Id, Name = team.Name, Slug = team.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        if (team == null)
        {
            return NotFound("That team could not be found.");
        }

        if (!await dbContext.TeamSpaces.AnyAsync(link => link.SpaceId == id && link.TeamId == teamId, cancellationToken))
        {
            var link = new TeamSpace { SpaceId = id, TeamId = teamId };
            dbContext.TeamSpaces.Add(link);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                dbContext.Entry(link).State = EntityState.Detached;
                if (!await dbContext.TeamSpaces.AnyAsync(existing => existing.SpaceId == id && existing.TeamId == teamId, cancellationToken))
                {
                    throw;
                }
            }
        }

        return Ok(new SpaceTeamAccessResponse
        {
            Team = team,
            ResourceCount = await dbContext.Resources.CountAsync(resource => resource.TeamId == teamId && resource.SpaceId == id,
                cancellationToken),
        });
    }

    [HttpDelete("{id:int}/teams/{teamId:int}")]
    public async Task<IActionResult> RemoveTeam(
        int id, int teamId, [FromQuery] int? confirmedResourceCount = null, CancellationToken cancellationToken = default)
    {
        if (confirmedResourceCount < 0)
        {
            return BadRequest("The confirmed resource count cannot be negative.");
        }

        try
        {
            return await RemoveTeamAssignment(id, teamId, confirmedResourceCount, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sqlException && sqlException.Number == 1205)
        {
            return TeamAssignmentChanged(id, teamId);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return TeamAssignmentChanged(id, teamId);
        }
    }

    private async Task<IActionResult> RemoveTeamAssignment(
        int id, int teamId, int? confirmedResourceCount, CancellationToken cancellationToken)
    {
        // Resource creation reads the same assignment in a serializable transaction.
        // Keep the count and removal together so new resources cannot bypass confirmation.
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (!await dbContext.Spaces.AnyAsync(space => space.Id == id, cancellationToken))
        {
            return NotFound("That space could not be found.");
        }
        if (!await dbContext.Teams.AnyAsync(team => team.Id == teamId, cancellationToken))
        {
            return NotFound("That team could not be found.");
        }

        var link = await dbContext.TeamSpaces.SingleOrDefaultAsync(link => link.SpaceId == id && link.TeamId == teamId,
            cancellationToken);
        if (link == null)
        {
            return NoContent();
        }
        var resourceCount = await dbContext.Resources.CountAsync(resource => resource.TeamId == teamId && resource.SpaceId == id,
            cancellationToken);
        if (resourceCount > 0 && confirmedResourceCount != resourceCount)
        {
            var resourceLabel = resourceCount == 1 ? "resource" : "resources";
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Confirm removing this team's space assignment.",
                Detail = $"This team has {resourceCount} {resourceLabel} in this space. Removing the assignment prevents new resources " +
                    "from being created here. Existing resources and reservations are kept, and the team can still manage its existing resources.",
                Extensions =
                {
                    ["code"] = "space_team_resources_confirmation_required",
                    ["resourceCount"] = resourceCount,
                },
            });
        }

        dbContext.TeamSpaces.Remove(link);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return NoContent();
    }

    private ConflictObjectResult TeamAssignmentChanged(int spaceId, int teamId)
    {
        var entry = dbContext.ChangeTracker.Entries<TeamSpace>()
            .SingleOrDefault(entry => entry.Entity.SpaceId == spaceId && entry.Entity.TeamId == teamId);
        if (entry != null)
        {
            entry.State = EntityState.Detached;
        }

        return Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The space assignment could not be removed.",
            Detail = "Another resource or assignment was saved at the same time. Refresh the team list and try again.",
            Extensions = { ["code"] = "space_team_access_changed" },
        });
    }

    private Task<bool> SlugExists(string slug, int? excludedId, CancellationToken cancellationToken)
        => dbContext.Spaces.AnyAsync(space => space.Slug == slug &&
            (excludedId == null || space.Id != excludedId), cancellationToken);

    private Task<bool> OfficialReferenceExists(string? referenceNumber, int? excludedId, CancellationToken cancellationToken)
        => dbContext.Spaces.AnyAsync(space => space.IsOfficialFacility && space.ReferenceNumber == referenceNumber &&
            (excludedId == null || space.Id != excludedId), cancellationToken);

    private async Task<string?> SaveSpace(Space space, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(space).State = EntityState.Detached;
            if (await SlugExists(space.Slug, space.Id, cancellationToken))
            {
                return "duplicate_slug";
            }
            if (space.IsOfficialFacility &&
                await OfficialReferenceExists(space.ReferenceNumber, space.Id, cancellationToken))
            {
                return "duplicate_reference_number";
            }

            throw;
        }
    }

    private ConflictObjectResult DuplicateConflict(string code)
        => Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "The space could not be saved.",
            Detail = code == "duplicate_slug"
                ? "That space slug is already in use. Choose another slug."
                : "That reference number is already in use by another official facility.",
            Extensions = { ["code"] = code },
        });

    private static string? ValidateRequest(SaveSpaceRequest request, out string name, out string slug)
    {
        name = request.Name?.Trim() ?? string.Empty;
        slug = request.Slug?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 200 || name.Any(char.IsControl))
        {
            return "Enter a space name of no more than 200 characters.";
        }

        if (TrimOptional(request.Address)?.Length > 500)
        {
            return "Enter an address of no more than 500 characters.";
        }
        var referenceNumber = TrimOptional(request.ReferenceNumber);
        if (referenceNumber?.Length > 100)
        {
            return "Enter a reference number of no more than 100 characters.";
        }
        if (request.IsOfficialFacility && referenceNumber == null)
        {
            return "A reference number is required for an official facility.";
        }
        if (slug.Length == 0 || slug.Length > 120 ||
            !Regex.IsMatch(slug, @"\A[a-z0-9]+(?:-[a-z0-9]+)*\z", RegexOptions.CultureInvariant))
        {
            return "Enter a slug of no more than 120 lowercase letters, numbers, and single hyphens between words.";
        }

        return null;
    }

    private static SpaceResponse ToResponse(Space space) => new()
    {
        Id = space.Id, Name = space.Name, Slug = space.Slug,
        Description = space.Description, Address = space.Address,
        IsOfficialFacility = space.IsOfficialFacility, IsActive = space.IsActive,
        ReferenceNumber = space.ReferenceNumber,
    };

    private static void ApplyEditableFields(Space space, SaveSpaceRequest request, string name)
    {
        space.Name = name;
        space.Description = TrimOptional(request.Description);
        space.Address = TrimOptional(request.Address);
        space.IsOfficialFacility = request.IsOfficialFacility;
        space.IsActive = request.IsActive;
        space.ReferenceNumber = TrimOptional(request.ReferenceNumber);
    }

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

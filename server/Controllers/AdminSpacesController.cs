using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Spaces;

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
        var error = ValidateRequest(request, isNew: true, out var name, out var slug);
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
        var error = ValidateRequest(request, isNew: false, out var name, out _);
        if (error != null)
        {
            return BadRequest(error);
        }

        var space = await dbContext.Spaces.SingleOrDefaultAsync(space => space.Id == id, cancellationToken);
        if (space == null)
        {
            return NotFound("That space could not be found.");
        }
        if (request.Slug != null && request.Slug != space.Slug)
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

    private static string? ValidateRequest(SaveSpaceRequest request, bool isNew, out string name, out string slug)
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
        if (!isNew)
        {
            return null;
        }

        if (request.Slug == null)
        {
            try
            {
                var normalized = Regex.Replace(name.Normalize(NormalizationForm.FormKD), @"[\u0300-\u036f]", string.Empty)
                    .ToLowerInvariant();
                slug = Regex.Replace(normalized, "[^a-z0-9]+", "-").Trim('-');
                slug = slug[..Math.Min(slug.Length, 120)].TrimEnd('-');
            }
            catch (ArgumentException)
            {
                return "Enter a valid space name.";
            }
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

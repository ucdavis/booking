using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Forms;
using Server.Helpers;
using Server.Models.ResourceTemplates;

namespace Server.Controllers;

[ApiController]
[Route("api/admin/resource-templates")]
[Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminResourceTemplatesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ResourceTemplateResponse>>> GetTemplates(CancellationToken cancellationToken = default)
    {
        var templates = await dbContext.ResourceTemplates.AsNoTracking()
            .Where(template => template.TeamId == null)
            .OrderBy(template => template.Name)
            .ThenBy(template => template.Id)
            .Select(template => new ResourceTemplateResponse
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                FormSchemaVersion = template.FormSchemaVersion,
                FormJson = template.FormJson,
                IsActive = template.IsActive,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(templates);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ResourceTemplateResponse>> GetTemplate(int id, CancellationToken cancellationToken = default)
    {
        var template = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id && template.TeamId == null, cancellationToken);
        if (template == null)
        {
            return NotFound("That resource template could not be found.");
        }

        return Ok(ToResponse(template));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ResourceTemplateResponse>> CreateTemplate(
        [FromBody] SaveResourceTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var error = ValidateRequest(request, out _);
        if (error != null)
        {
            return BadRequest(error);
        }
        if (request.FormSchemaVersion != 1)
        {
            return BadRequest("A new template must start at version 1.");
        }
        var userId = await GetCurrentUserId(cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var template = new ResourceTemplate
        {
            TeamId = null,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            FormSchemaVersion = 1,
            FormJson = request.FormJson,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = userId.Value,
        };
        dbContext.ResourceTemplates.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, ToResponse(template));
    }

    [HttpPut("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ResourceTemplateResponse>> UpdateTemplate(
        int id, [FromBody] SaveResourceTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var error = ValidateRequest(request, out var requestedForm);
        if (error != null)
        {
            return BadRequest(error);
        }
        var userId = await GetCurrentUserId(cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }

        var template = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id && template.TeamId == null, cancellationToken);
        if (template == null)
        {
            return NotFound("That resource template could not be found.");
        }

        if (!template.IsActive)
        {
            return Conflict("Archived templates cannot be edited. Duplicate this template to create an active copy.");
        }
        if (request.UpdatedAt == null)
        {
            return BadRequest("The saved template timestamp is required when updating a template.");
        }
        if (template.FormSchemaVersion != request.FormSchemaVersion || template.UpdatedAt != request.UpdatedAt.Value)
        {
            return Conflict("This template has changed since you opened it. Reload it before saving again.");
        }
        if (!FormDefinitionValidator.TryParse(template.FormSchemaVersion, template.FormJson, out var savedForm, out error))
        {
            return BadRequest(error);
        }

        var formChanged = !FormDefinitionValidator.AreEquivalent(savedForm!, requestedForm!);
        if (formChanged && template.FormSchemaVersion == int.MaxValue)
        {
            return Conflict("This template has reached the maximum supported version and cannot create another revision.");
        }
        var now = DateTimeOffset.UtcNow;
        if (now <= template.UpdatedAt)
        {
            if (template.UpdatedAt.Ticks == DateTimeOffset.MaxValue.Ticks)
            {
                return Conflict("This template timestamp cannot advance. The template has not been changed.");
            }
            now = template.UpdatedAt.AddTicks(1);
        }
        var name = formChanged ? template.Name : request.Name.Trim();
        var requestedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var description = formChanged ? template.Description : requestedDescription;
        var isActive = !formChanged && request.IsActive;
        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Claim this exact saved state before inserting a revision. The transaction keeps
        // the archive and insert atomic, and the timestamp predicate rejects stale writers.
        var original = dbContext.ResourceTemplates.Where(current => current.Id == id && current.TeamId == null && current.IsActive &&
            current.FormSchemaVersion == request.FormSchemaVersion && current.UpdatedAt == request.UpdatedAt.Value);
        if (isRelational)
        {
            var updated = await original.ExecuteUpdateAsync(setters => setters
                .SetProperty(current => current.Name, name)
                .SetProperty(current => current.Description, description)
                .SetProperty(current => current.IsActive, isActive)
                .SetProperty(current => current.UpdatedAt, now)
                .SetProperty(current => current.UpdatedByUserId, userId.Value), cancellationToken);
            if (updated == 0)
            {
                return Conflict("This template has changed since you opened it. Reload it before saving again.");
            }
        }
        else
        {
            var tracked = await original.SingleOrDefaultAsync(cancellationToken);
            if (tracked == null)
            {
                return Conflict("This template has changed since you opened it. Reload it before saving again.");
            }
            tracked.Name = name;
            tracked.Description = description;
            tracked.IsActive = isActive;
            tracked.UpdatedAt = now;
            tracked.UpdatedByUserId = userId.Value;
        }

        ResourceTemplate result;
        if (formChanged)
        {
            result = new ResourceTemplate
            {
                TeamId = null,
                Name = request.Name.Trim(),
                Description = requestedDescription,
                FormSchemaVersion = template.FormSchemaVersion + 1,
                FormJson = request.FormJson,
                ResourceDefaultsJson = template.ResourceDefaultsJson,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByUserId = userId.Value,
            };
            dbContext.ResourceTemplates.Add(result);
        }
        else
        {
            // Preserve the exact original JSON when only presentation or metadata changed.
            template.Name = name;
            template.Description = description;
            template.IsActive = isActive;
            template.UpdatedAt = now;
            template.UpdatedByUserId = userId.Value;
            result = template;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (formChanged)
        {
            return CreatedAtAction(nameof(GetTemplate), new { id = result.Id }, ToResponse(result));
        }
        return Ok(ToResponse(result));
    }

    [HttpPost("{id:int}/duplicate")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ResourceTemplateResponse>> DuplicateTemplate(
        int id, CancellationToken cancellationToken = default)
    {
        var userId = await GetCurrentUserId(cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }
        var source = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id && template.TeamId == null, cancellationToken);
        if (source == null)
        {
            return NotFound("That resource template could not be found.");
        }
        if (!FormDefinitionValidator.TryParse(source.FormSchemaVersion, source.FormJson, out _, out var error))
        {
            return BadRequest(error);
        }

        const string suffix = " (copy)";
        var name = source.Name.Length > 200 - suffix.Length ? source.Name[..(200 - suffix.Length)] : source.Name;
        var now = DateTimeOffset.UtcNow;
        var duplicate = new ResourceTemplate
        {
            TeamId = null,
            Name = name + suffix,
            Description = source.Description,
            FormSchemaVersion = 1,
            FormJson = source.FormJson,
            ResourceDefaultsJson = source.ResourceDefaultsJson,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = userId.Value,
        };
        dbContext.ResourceTemplates.Add(duplicate);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetTemplate), new { id = duplicate.Id }, ToResponse(duplicate));
    }

    private async Task<int?> GetCurrentUserId(CancellationToken cancellationToken)
    {
        var iamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(iamId))
        {
            return null;
        }

        return await dbContext.Users.AsNoTracking()
            .Where(user => user.IamId == iamId && user.IsActive && user.IsAdmin)
            .Select(user => (int?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string? ValidateRequest(SaveResourceTemplateRequest? request, out FormDefinition? definition)
    {
        definition = null;
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return "Enter a template name of no more than 200 characters.";
        }
        if (request.Description?.Length > 2000)
        {
            return "Enter a template description of no more than 2,000 characters.";
        }

        return FormDefinitionValidator.TryParse(request.FormSchemaVersion, request.FormJson, out definition, out var error)
            ? null : error;
    }

    private static ResourceTemplateResponse ToResponse(ResourceTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        Description = template.Description,
        FormSchemaVersion = template.FormSchemaVersion,
        FormJson = template.FormJson,
        IsActive = template.IsActive,
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
    };
}

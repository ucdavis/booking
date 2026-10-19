using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Forms;
using Server.Models.ResourceTemplates;

namespace Server.Controllers;

public abstract class ResourceTemplatesControllerBase(AppDbContext dbContext) : ControllerBase
{
    protected AppDbContext DbContext => dbContext;

    protected async Task<ActionResult<List<ResourceTemplateResponse>>> ReadTemplates(
        int? teamId, bool includeActiveSystem, CancellationToken cancellationToken)
    {
        var templates = await dbContext.ResourceTemplates.AsNoTracking()
            .Where(template => template.TeamId == teamId || includeActiveSystem && template.TeamId == null && template.IsActive)
            .OrderBy(template => template.Name)
            .ThenBy(template => template.Id)
            .Select(template => new ResourceTemplateResponse
            {
                Id = template.Id,
                TeamId = template.TeamId,
                Name = template.Name,
                Description = template.Description,
                FormSchemaVersion = template.FormSchemaVersion,
                FormJson = template.FormJson,
                ResourceDefaultsJson = template.ResourceDefaultsJson,
                IsActive = template.IsActive,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(templates);
    }

    protected async Task<ActionResult<ResourceTemplateResponse>> ReadTemplate(
        int id, int? teamId, bool includeActiveSystem, CancellationToken cancellationToken)
    {
        var template = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id &&
                (template.TeamId == teamId || includeActiveSystem && template.TeamId == null && template.IsActive), cancellationToken);
        if (template == null)
        {
            return NotFound("That resource template could not be found.");
        }

        return Ok(ToResponse(template));
    }

    protected async Task<ActionResult<ResourceTemplateResponse>> CreateTemplateForScope(
        SaveResourceTemplateRequest request, int? teamId, string? teamSlug, CancellationToken cancellationToken)
    {
        var error = ValidateRequest(request, out _, out var resourceDefaultsJson);
        if (error != null)
        {
            return BadRequest(error);
        }
        if (request.FormSchemaVersion != 1)
        {
            return BadRequest("A new template must start at version 1.");
        }
        var userId = await GetCurrentUserId(teamId, cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var template = new ResourceTemplate
        {
            TeamId = teamId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            FormSchemaVersion = 1,
            FormJson = request.FormJson,
            ResourceDefaultsJson = resourceDefaultsJson,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = userId.Value,
        };
        dbContext.ResourceTemplates.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedTemplate(template, teamSlug);
    }

    protected async Task<ActionResult<ResourceTemplateResponse>> UpdateTemplateForScope(
        int id, SaveResourceTemplateRequest request, int? teamId, string? teamSlug, CancellationToken cancellationToken)
    {
        var error = ValidateRequest(request, out var requestedForm, out var requestedDefaultsJson);
        if (error != null)
        {
            return BadRequest(error);
        }
        var userId = await GetCurrentUserId(teamId, cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }

        var template = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id && template.TeamId == teamId, cancellationToken);
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

        if (!request.HasResourceDefaultsJson)
        {
            requestedDefaultsJson = template.ResourceDefaultsJson;
        }
        var formChanged = !FormDefinitionValidator.AreEquivalent(savedForm!, requestedForm!);
        var defaultsChanged = !string.Equals(template.ResourceDefaultsJson, requestedDefaultsJson, StringComparison.Ordinal);
        var revisionChanged = formChanged || defaultsChanged;
        if (revisionChanged && template.FormSchemaVersion == int.MaxValue)
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
        var name = revisionChanged ? template.Name : request.Name.Trim();
        var requestedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var description = revisionChanged ? template.Description : requestedDescription;
        var resourceDefaultsJson = template.ResourceDefaultsJson;
        var isActive = !revisionChanged && request.IsActive;
        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Claim this exact saved state before inserting a revision. The transaction keeps
        // the archive and insert atomic, and the timestamp predicate rejects stale writers.
        var original = dbContext.ResourceTemplates.Where(current => current.Id == id && current.TeamId == teamId && current.IsActive &&
            current.FormSchemaVersion == request.FormSchemaVersion && current.UpdatedAt == request.UpdatedAt.Value);
        if (isRelational)
        {
            var updated = await original.ExecuteUpdateAsync(setters => setters
                .SetProperty(current => current.Name, name)
                .SetProperty(current => current.Description, description)
                .SetProperty(current => current.ResourceDefaultsJson, resourceDefaultsJson)
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
            tracked.ResourceDefaultsJson = resourceDefaultsJson;
            tracked.IsActive = isActive;
            tracked.UpdatedAt = now;
            tracked.UpdatedByUserId = userId.Value;
        }

        ResourceTemplate result;
        if (revisionChanged)
        {
            result = new ResourceTemplate
            {
                TeamId = teamId,
                Name = request.Name.Trim(),
                Description = requestedDescription,
                FormSchemaVersion = template.FormSchemaVersion + 1,
                FormJson = formChanged ? request.FormJson : template.FormJson,
                ResourceDefaultsJson = requestedDefaultsJson,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByUserId = userId.Value,
            };
            dbContext.ResourceTemplates.Add(result);
        }
        else
        {
            // Preserve the exact original form JSON when only presentation or metadata changed.
            template.Name = name;
            template.Description = description;
            template.ResourceDefaultsJson = resourceDefaultsJson;
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

        if (revisionChanged)
        {
            return CreatedTemplate(result, teamSlug);
        }
        return Ok(ToResponse(result));
    }

    protected async Task<ActionResult<ResourceTemplateResponse>> DuplicateTemplateForScope(
        int id, int? teamId, bool includeActiveSystem, string? teamSlug, CancellationToken cancellationToken)
    {
        var userId = await GetCurrentUserId(teamId, cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }
        var source = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id &&
                (template.TeamId == teamId || includeActiveSystem && template.TeamId == null && template.IsActive), cancellationToken);
        if (source == null)
        {
            return NotFound("That resource template could not be found.");
        }
        var error = ValidateFormForSave(source.FormSchemaVersion, source.FormJson, out _);
        if (error != null)
        {
            return BadRequest(error);
        }

        const string suffix = " (copy)";
        var name = source.Name.Length > 200 - suffix.Length ? source.Name[..(200 - suffix.Length)] : source.Name;
        var now = DateTimeOffset.UtcNow;
        var duplicate = new ResourceTemplate
        {
            TeamId = teamId,
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

        return CreatedTemplate(duplicate, teamSlug);
    }

    private CreatedAtActionResult CreatedTemplate(ResourceTemplate template, string? teamSlug)
    {
        var routeValues = new Dictionary<string, object?> { ["id"] = template.Id };
        if (teamSlug != null)
        {
            routeValues["teamSlug"] = teamSlug;
        }
        return CreatedAtAction("GetTemplate", routeValues, ToResponse(template));
    }

    private async Task<int?> GetCurrentUserId(int? teamId, CancellationToken cancellationToken)
    {
        var iamId = User.FindFirst("ucdPersonIAMID")?.Value;
        if (User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(iamId))
        {
            return null;
        }

        return await dbContext.Users.AsNoTracking()
            .Where(user => user.IamId == iamId && user.IsActive && (teamId != null || user.IsAdmin))
            .Select(user => (int?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string? ValidateRequest(SaveResourceTemplateRequest? request, out FormDefinition? definition,
        out string? resourceDefaultsJson)
    {
        definition = null;
        resourceDefaultsJson = null;
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return "Enter a template name of no more than 200 characters.";
        }
        if (request.Description?.Length > 2000)
        {
            return "Enter a template description of no more than 2,000 characters.";
        }
        if (request.ResourceDefaultsJson?.Length > SaveResourceTemplateRequest.MaxResourceDefaultsJsonLength)
        {
            return "Resource defaults cannot exceed 1,048,576 characters.";
        }
        if (!string.IsNullOrWhiteSpace(request.ResourceDefaultsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(request.ResourceDefaultsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object && document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return "Resource defaults must be a valid JSON object or array.";
                }
            }
            catch (JsonException)
            {
                return "Resource defaults must be a valid JSON object or array.";
            }
            resourceDefaultsJson = request.ResourceDefaultsJson;
        }

        return ValidateFormForSave(request.FormSchemaVersion, request.FormJson, out definition);
    }

    private static string? ValidateFormForSave(int schemaVersion, string formJson, out FormDefinition? definition)
    {
        if (!FormDefinitionValidator.TryParse(schemaVersion, formJson, out definition, out var error))
        {
            return error;
        }

        return definition!.Fields.Count > 0
            ? null
            : "Add at least one form field before saving the template.";
    }

    private static ResourceTemplateResponse ToResponse(ResourceTemplate template) => new()
    {
        Id = template.Id,
        TeamId = template.TeamId,
        Name = template.Name,
        Description = template.Description,
        FormSchemaVersion = template.FormSchemaVersion,
        FormJson = template.FormJson,
        ResourceDefaultsJson = template.ResourceDefaultsJson,
        IsActive = template.IsActive,
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
    };
}

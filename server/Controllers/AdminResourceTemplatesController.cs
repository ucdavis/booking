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
        var error = ValidateRequest(request);
        if (error != null)
        {
            return BadRequest(error);
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
            FormSchemaVersion = request.FormSchemaVersion,
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
        var error = ValidateRequest(request);
        if (error != null)
        {
            return BadRequest(error);
        }
        var userId = await GetCurrentUserId(cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }

        var template = await dbContext.ResourceTemplates
            .SingleOrDefaultAsync(template => template.Id == id && template.TeamId == null, cancellationToken);
        if (template == null)
        {
            return NotFound("That resource template could not be found.");
        }

        if (template.FormSchemaVersion != FormDefinitionValidator.CurrentSchemaVersion)
        {
            return BadRequest("This saved form schema version is not supported by this editor.");
        }

        template.Name = request.Name.Trim();
        template.FormSchemaVersion = request.FormSchemaVersion;
        template.FormJson = request.FormJson;
        template.IsActive = request.IsActive;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedByUserId = userId.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(template));
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
            FormSchemaVersion = source.FormSchemaVersion,
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

    private static string? ValidateRequest(SaveResourceTemplateRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return "Enter a template name of no more than 200 characters.";
        }

        return FormDefinitionValidator.TryParse(request.FormSchemaVersion, request.FormJson, out _, out var error)
            ? null : error;
    }

    private static ResourceTemplateResponse ToResponse(ResourceTemplate template) => new()
    {
        Id = template.Id,
        Name = template.Name,
        FormSchemaVersion = template.FormSchemaVersion,
        FormJson = template.FormJson,
        IsActive = template.IsActive,
        CreatedAt = template.CreatedAt,
        UpdatedAt = template.UpdatedAt,
    };
}

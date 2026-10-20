using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Core.Data;
using Server.Helpers;
using Server.Models.ResourceTemplates;

namespace Server.Controllers;

[ApiController]
[Route("api/admin/resource-templates")]
[Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminResourceTemplatesController(AppDbContext dbContext) : ResourceTemplatesControllerBase(dbContext)
{
    [HttpGet]
    public Task<ActionResult<List<ResourceTemplateResponse>>> GetTemplates(CancellationToken cancellationToken = default)
        => ReadTemplates(null, false, cancellationToken);

    [HttpGet("{id:int}")]
    public Task<ActionResult<ResourceTemplateResponse>> GetTemplate(int id, CancellationToken cancellationToken = default)
        => ReadTemplate(id, null, false, cancellationToken);

    [HttpPost]
    public Task<ActionResult<ResourceTemplateResponse>> CreateTemplate(
        [FromBody] SaveResourceTemplateRequest request, CancellationToken cancellationToken = default)
        => CreateTemplateForScope(request, null, null, cancellationToken);

    [HttpPut("{id:int}")]
    public Task<ActionResult<ResourceTemplateResponse>> UpdateTemplate(
        int id, [FromBody] SaveResourceTemplateRequest request, CancellationToken cancellationToken = default)
        => UpdateTemplateForScope(id, request, null, null, cancellationToken);

    [HttpPost("{id:int}/duplicate")]
    public Task<ActionResult<ResourceTemplateResponse>> DuplicateTemplate(
        int id, CancellationToken cancellationToken = default)
        => DuplicateTemplateForScope(id, null, false, null, cancellationToken);
}

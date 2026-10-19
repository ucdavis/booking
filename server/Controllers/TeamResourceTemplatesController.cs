using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Helpers;
using Server.Models.ResourceTemplates;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/teams/{teamSlug}/resource-templates")]
[Authorize(Policy = AuthenticationHelper.TeamEditorPolicy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeamResourceTemplatesController(AppDbContext dbContext, TeamAccessService teamAccessService)
    : ResourceTemplatesControllerBase(dbContext)
{
    [HttpGet]
    public Task<ActionResult<List<ResourceTemplateResponse>>> GetTemplates(
        string teamSlug, CancellationToken cancellationToken = default)
        => ForTeam(teamSlug, teamId => ReadTemplates(teamId, true, cancellationToken), cancellationToken);

    [HttpGet("{id:int}")]
    public Task<ActionResult<ResourceTemplateResponse>> GetTemplate(
        string teamSlug, int id, CancellationToken cancellationToken = default)
        => ForTeam(teamSlug, teamId => ReadTemplate(id, teamId, true, cancellationToken), cancellationToken);

    [HttpPost]
    public Task<ActionResult<ResourceTemplateResponse>> CreateTemplate(
        string teamSlug, [FromBody] SaveResourceTemplateRequest request, CancellationToken cancellationToken = default)
        => ForTeam(teamSlug, teamId => CreateTemplateForScope(request, teamId, teamSlug, cancellationToken), cancellationToken);

    [HttpPut("{id:int}")]
    public Task<ActionResult<ResourceTemplateResponse>> UpdateTemplate(
        string teamSlug, int id, [FromBody] SaveResourceTemplateRequest request, CancellationToken cancellationToken = default)
        => ForTeam(teamSlug, teamId => UpdateTemplateForScope(id, request, teamId, teamSlug, cancellationToken), cancellationToken);

    [HttpPost("{id:int}/duplicate")]
    public Task<ActionResult<ResourceTemplateResponse>> DuplicateTemplate(
        string teamSlug, int id, CancellationToken cancellationToken = default)
        => ForTeam(teamSlug, teamId => DuplicateTemplateForScope(id, teamId, true, teamSlug, cancellationToken), cancellationToken);

    private async Task<ActionResult<T>> ForTeam<T>(
        string teamSlug, Func<int, Task<ActionResult<T>>> action, CancellationToken cancellationToken)
    {
        if (!await teamAccessService.CanEditTeam(User, teamSlug, cancellationToken))
        {
            return Forbid();
        }

        var teamId = await DbContext.Teams.AsNoTracking()
            .Where(team => team.Slug == teamSlug)
            .Select(team => (int?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (teamId == null)
        {
            return NotFound("That team could not be found.");
        }

        return await action(teamId.Value);
    }
}

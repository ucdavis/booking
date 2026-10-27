using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Helpers;
using Server.Models.Resources;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/teams/{teamSlug}/resources")]
[Authorize(Policy = AuthenticationHelper.TeamEditorPolicy)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeamResourcesController(
    AppDbContext dbContext, TeamAccessService teamAccessService, TeamResourceService resourceService) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<List<ResourceSummaryResponse>>> GetResources(
        string teamSlug, CancellationToken cancellationToken = default)
        => ForTeam<List<ResourceSummaryResponse>>(teamSlug,
            async (teamId, _) => Ok(await resourceService.GetResources(teamId, cancellationToken)), cancellationToken);

    [HttpGet("options")]
    public Task<ActionResult<ResourceOptionsResponse>> GetOptions(
        string teamSlug, CancellationToken cancellationToken = default)
        => ForTeam<ResourceOptionsResponse>(teamSlug,
            async (teamId, _) => Ok(await resourceService.GetOptions(teamId, cancellationToken)), cancellationToken);

    [HttpGet("{id:int}")]
    public Task<ActionResult<ResourceDetailResponse>> GetResource(
        string teamSlug, int id, CancellationToken cancellationToken = default)
        => ForTeam<ResourceDetailResponse>(teamSlug, async (teamId, _) =>
        {
            var resource = await resourceService.GetResource(teamId, id, cancellationToken);
            return resource == null ? NotFound("That resource could not be found.") : Ok(resource);
        }, cancellationToken);

    [HttpPost]
    public Task<ActionResult<ResourceDetailResponse>> CreateResource(
        string teamSlug, [FromBody] SaveResourceRequest request, CancellationToken cancellationToken = default)
        => ForTeam<ResourceDetailResponse>(teamSlug, async (teamId, userId) =>
        {
            var result = await resourceService.SaveResource(teamId, userId, null, request, cancellationToken);
            return result.Resource == null
                ? SaveError(result.StatusCode, result.Error)
                : CreatedAtAction(nameof(GetResource), new { teamSlug, id = result.Resource.Id }, result.Resource);
        }, cancellationToken);

    [HttpPut("{id:int}")]
    public Task<ActionResult<ResourceDetailResponse>> UpdateResource(
        string teamSlug, int id, [FromBody] SaveResourceRequest request, CancellationToken cancellationToken = default)
        => ForTeam<ResourceDetailResponse>(teamSlug, async (teamId, userId) =>
        {
            var result = await resourceService.SaveResource(teamId, userId, id, request, cancellationToken);
            return result.Resource == null ? SaveError(result.StatusCode, result.Error) : Ok(result.Resource);
        }, cancellationToken);

    private ObjectResult SaveError(int statusCode, string? error)
        => statusCode switch
        {
            400 => BadRequest(error),
            404 => NotFound(error),
            409 => Conflict(error),
            _ => StatusCode(statusCode, error),
        };

    private async Task<ActionResult<T>> ForTeam<T>(
        string teamSlug, Func<int, int, Task<ActionResult<T>>> action, CancellationToken cancellationToken)
    {
        if (!await teamAccessService.CanEditTeam(User, teamSlug, cancellationToken))
        {
            return Forbid();
        }

        var teamId = await dbContext.Teams.AsNoTracking().Where(team => team.Slug == teamSlug)
            .Select(team => (int?)team.Id).SingleOrDefaultAsync(cancellationToken);
        if (teamId == null)
        {
            return NotFound("That team could not be found.");
        }

        var iamId = User.FindFirst("ucdPersonIAMID")?.Value;
        var userId = await dbContext.Users.AsNoTracking().Where(user => user.IamId == iamId && user.IsActive)
            .Select(user => (int?)user.Id).SingleOrDefaultAsync(cancellationToken);
        if (userId == null)
        {
            return Forbid();
        }

        return await action(teamId.Value, userId.Value);
    }
}

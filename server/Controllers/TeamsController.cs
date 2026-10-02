using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helpers;
using Server.Models.Teams;
using Server.Services;

namespace Server.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeamsController(TeamAccessService teamAccessService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TeamSummaryResponse>>> GetTeams(CancellationToken cancellationToken = default)
        => Ok(await teamAccessService.GetMemberships(User, cancellationToken));

    [HttpGet("{teamSlug}")]
    [Authorize(Policy = AuthenticationHelper.TeamAccessPolicy)]
    public async Task<ActionResult<TeamAccessResponse>> GetTeam(
        string teamSlug, CancellationToken cancellationToken = default)
    {
        var access = await teamAccessService.GetAccess(User, teamSlug, cancellationToken);
        if (access == null)
        {
            return NotFound("That team could not be found or is no longer accessible.");
        }

        return Ok(access);
    }
}

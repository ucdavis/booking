using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Helpers;
using Server.Models.Spaces;
using Server.Models.Teams;
using Server.Services;

namespace Server.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TeamsController(TeamAccessService teamAccessService, AppDbContext dbContext) : ApiControllerBase
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

    [HttpGet("{teamSlug}/spaces")]
    [Authorize(Policy = AuthenticationHelper.TeamAccessPolicy)]
    public async Task<ActionResult<List<SpaceResponse>>> GetSpaces(
        string teamSlug, CancellationToken cancellationToken = default)
    {
        var access = await teamAccessService.GetAccess(User, teamSlug, cancellationToken);
        if (access == null)
        {
            return NotFound("That team could not be found or is no longer accessible.");
        }

        var spaces = await dbContext.TeamSpaces.AsNoTracking()
            .Where(link => link.TeamId == access.Team.Id)
            .OrderBy(link => link.Space.Name)
            .ThenBy(link => link.Space.Slug)
            .Select(link => new SpaceResponse
            {
                Id = link.SpaceId, Name = link.Space.Name, Slug = link.Space.Slug,
                Description = link.Space.Description, Address = link.Space.Address,
                IsOfficialFacility = link.Space.IsOfficialFacility, IsActive = link.Space.IsActive,
                ReferenceNumber = link.Space.ReferenceNumber,
            })
            .ToListAsync(cancellationToken);

        return Ok(spaces);
    }
}

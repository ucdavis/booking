using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Helpers;
using Server.Models.Emulation;
using Server.Services;

namespace Server.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmulationController(EmulationService emulationService, IUserService userService) : ApiControllerBase
{
    [HttpGet("candidates")]
    [Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
    public async Task<ActionResult<List<EmulationCandidateResponse>>> Candidates(
        [FromQuery] string? query, CancellationToken cancellationToken = default)
    {
        if (EmulationService.IsEmulating(HttpContext))
        {
            return Conflict("Stop emulating before choosing another user.");
        }
        if (!await userService.IsSiteAdmin(EmulationService.GetActor(HttpContext), cancellationToken))
        {
            return Forbid();
        }

        var search = query?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length > 128)
        {
            return BadRequest("Enter an email, IAM ID, or Kerberos ID of no more than 128 characters.");
        }

        return Ok(await emulationService.SearchAsync(search, cancellationToken));
    }

    [HttpGet("antiforgery")]
    public IActionResult Antiforgery([FromServices] IAntiforgery antiforgery)
        => Ok(new { Token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("start")]
    [Authorize(Policy = AuthenticationHelper.SiteAdminPolicy)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
        [FromBody] StartEmulationRequest request, CancellationToken cancellationToken = default)
    {
        if (EmulationService.IsEmulating(HttpContext))
        {
            return Conflict("Stop emulating before choosing another user.");
        }

        var actor = EmulationService.GetActor(HttpContext);
        var sessionId = HttpContext.Items[EmulationService.SessionItemKey] as string;
        if (string.IsNullOrWhiteSpace(sessionId) || !await userService.IsSiteAdmin(actor, cancellationToken))
        {
            return Forbid();
        }

        var iamId = request.IamId?.Trim();
        if (string.IsNullOrEmpty(iamId) || iamId.Length > 50)
        {
            return BadRequest("A valid IAM ID is required.");
        }

        var (target, error) = await emulationService.FindOrCreateTargetAsync(iamId, cancellationToken);
        if (error != null)
        {
            return Conflict(error);
        }
        if (target == null)
        {
            return NotFound("That user or person could not be found. Search again before emulating.");
        }

        emulationService.Start(HttpContext, actor, sessionId, target);
        return NoContent();
    }

    [HttpPost("stop")]
    [ValidateAntiForgeryToken]
    public IActionResult Stop()
    {
        emulationService.Clear(HttpContext, force: true);
        return NoContent();
    }
}

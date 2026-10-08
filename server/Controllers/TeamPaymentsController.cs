using Azure;
using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Payments;
using Server.Models.Teams;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/teams/{teamSlug}/payments")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TeamPaymentsController(
    AppDbContext dbContext, TeamAccessService teamAccessService,
    ISecretsService secretsService, IPaymentsService paymentsService) : ControllerBase
{
    private const string InvalidKeyMessage = "The Payments API key is invalid or has been disabled. Ask a team administrator to replace it.";
    private const string UnavailableMessage = "The Payments connection could not be checked right now. Try again later.";
    private const string SaveUnavailableMessage = "The Payments connection could not be saved right now. Your existing connection has not been changed. Try again later.";

    [HttpGet]
    [Authorize(Policy = AuthenticationHelper.TeamAccessPolicy)]
    public async Task<ActionResult<TeamPaymentsResponse>> GetPayments(
        string teamSlug, CancellationToken cancellationToken = default)
    {
        var access = await teamAccessService.GetAccess(User, teamSlug, cancellationToken);
        if (access == null)
        {
            return NotFound("That team could not be found or is no longer accessible.");
        }

        var team = await dbContext.Teams.AsNoTracking()
            .SingleOrDefaultAsync(team => team.Id == access.Team.Id, cancellationToken);
        if (team == null)
        {
            return NotFound("That team could not be found.");
        }
        if (string.IsNullOrWhiteSpace(team.PaymentsApiKeySecretName))
        {
            return Ok(new TeamPaymentsResponse { Status = "unconfigured" });
        }

        string? maskedApiKey = null;
        try
        {
            var apiKey = await secretsService.GetSecretAsync(team.PaymentsApiKeySecretName, cancellationToken);
            if (access.IsSiteAdmin || access.Role == TeamRole.Admin)
            {
                maskedApiKey = MaskApiKey(apiKey);
            }

            var paymentsTeam = await paymentsService.GetTeamForApiKeyAsync(apiKey, cancellationToken);
            if (paymentsTeam == null)
            {
                return Ok(new TeamPaymentsResponse
                {
                    Status = "invalid", PaymentsTeamSlug = team.PaymentsTeamSlug,
                    MaskedApiKey = maskedApiKey, Message = InvalidKeyMessage,
                });
            }
            if (!string.IsNullOrWhiteSpace(team.PaymentsTeamSlug) &&
                !string.Equals(paymentsTeam.Slug, team.PaymentsTeamSlug, StringComparison.Ordinal))
            {
                return Ok(new TeamPaymentsResponse
                {
                    Status = "invalid", PaymentsTeamSlug = team.PaymentsTeamSlug,
                    MaskedApiKey = maskedApiKey,
                    Message = "The saved Payments key belongs to a different Payments team. Ask a team administrator to reconnect it.",
                });
            }

            return Ok(ToValidResponse(paymentsTeam, maskedApiKey));
        }
        catch (Exception exception) when (IsConnectionFailure(exception, cancellationToken))
        {
            return Ok(new TeamPaymentsResponse
            {
                Status = "unavailable", PaymentsTeamSlug = team.PaymentsTeamSlug,
                MaskedApiKey = maskedApiKey, Message = UnavailableMessage,
            });
        }
    }

    [HttpPut]
    [Authorize(Policy = AuthenticationHelper.TeamAdminPolicy)]
    public async Task<ActionResult<TeamPaymentsResponse>> SavePayments(
        string teamSlug, [FromBody] SaveTeamPaymentsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await teamAccessService.CanAdministerTeam(User, teamSlug, cancellationToken))
        {
            return Forbid();
        }

        var rawApiKey = request.ApiKey ?? string.Empty;
        var apiKey = rawApiKey.Trim();
        if (string.IsNullOrEmpty(apiKey) || rawApiKey.Length > 4096 || rawApiKey.Any(char.IsControl))
        {
            return BadRequest("Enter a Payments API key of no more than 4096 characters without control characters.");
        }

        var team = await dbContext.Teams.SingleOrDefaultAsync(team => team.Slug == teamSlug, cancellationToken);
        if (team == null)
        {
            return NotFound("That team could not be found.");
        }

        PaymentsTeam? paymentsTeam;
        string secretName;
        try
        {
            paymentsTeam = await paymentsService.GetTeamForApiKeyAsync(apiKey, cancellationToken);
            if (paymentsTeam == null)
            {
                return BadRequest("This Payments API key is invalid or has been disabled. The connection has not been changed.");
            }

            // A new secret per save keeps simultaneous replacements from mixing a key and team slug.
            secretName = $"team-{team.Id}-payments-{Guid.NewGuid():N}";
            await secretsService.SetSecretAsync(secretName, apiKey, cancellationToken);
        }
        catch (Exception exception) when (IsConnectionFailure(exception, cancellationToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, SaveUnavailableMessage);
        }

        var previousSlug = team.PaymentsTeamSlug;
        var previousSecretName = team.PaymentsApiKeySecretName;
        var previousUpdatedAt = team.UpdatedAt;
        team.PaymentsTeamSlug = paymentsTeam.Slug;
        team.PaymentsApiKeySecretName = secretName;
        team.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Preserve both secrets because a database failure can leave the commit outcome uncertain.
            team.PaymentsTeamSlug = previousSlug;
            team.PaymentsApiKeySecretName = previousSecretName;
            team.UpdatedAt = previousUpdatedAt;
            var entry = dbContext.Entry(team);
            entry.Property(value => value.PaymentsTeamSlug).IsModified = false;
            entry.Property(value => value.PaymentsApiKeySecretName).IsModified = false;
            entry.Property(value => value.UpdatedAt).IsModified = false;
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "The Payments save could not be confirmed. Check the connection before trying again.");
        }

        return Ok(ToValidResponse(paymentsTeam, MaskApiKey(apiKey)));
    }

    private static TeamPaymentsResponse ToValidResponse(PaymentsTeam team, string? maskedApiKey) => new()
    {
        Status = "valid", PaymentsTeamSlug = team.Slug, PaymentsTeamName = team.Name, MaskedApiKey = maskedApiKey,
    };

    private static string MaskApiKey(string apiKey)
        => apiKey.Length <= 6 ? new string('*', apiKey.Length)
            : apiKey[..3] + new string('*', apiKey.Length - 6) + apiKey[^3..];

    private static bool IsConnectionFailure(Exception exception, CancellationToken cancellationToken)
        => exception is RequestFailedException || exception is AuthenticationFailedException ||
            exception is HttpRequestException || exception is InvalidOperationException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}

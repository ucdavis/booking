namespace Server.Models.Teams;

public sealed class TeamPaymentsResponse
{
    public required string Status { get; init; }
    public string? PaymentsTeamSlug { get; init; }
    public string? PaymentsTeamName { get; init; }
    public string? MaskedApiKey { get; init; }
    public string? Message { get; init; }
}

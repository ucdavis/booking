namespace Server.Models.Teams;

public sealed class TeamAccessResponse
{
    public required TeamSummaryResponse Team { get; init; }
    public string? Role { get; init; }
    public bool IsSiteAdmin { get; init; }
}

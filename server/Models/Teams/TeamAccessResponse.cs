using Server.Core.Domain;

namespace Server.Models.Teams;

public sealed class TeamAccessResponse
{
    public required TeamSummaryResponse Team { get; init; }
    public List<TeamRole> Roles { get; init; } = [];
    public bool IsSiteAdmin { get; init; }
}

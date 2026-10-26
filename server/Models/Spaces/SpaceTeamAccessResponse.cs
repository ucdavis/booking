using Server.Models.Teams;

namespace Server.Models.Spaces;

public sealed class SpaceTeamAccessResponse
{
    public required TeamSummaryResponse Team { get; init; }
    public int ResourceCount { get; init; }
}

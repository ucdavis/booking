using Server.Core.Domain;

namespace Server.Models.Teams;

public sealed class TeamSummaryResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public List<TeamRole> Roles { get; init; } = [];
}

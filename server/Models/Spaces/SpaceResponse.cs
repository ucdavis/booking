using Server.Models.Teams;

namespace Server.Models.Spaces;

public sealed class SpaceResponse
{
    public List<TeamSummaryResponse> Teams { get; init; } = [];
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public string? Description { get; init; }
    public string? Address { get; init; }
    public bool IsOfficialFacility { get; init; }
    public bool IsActive { get; init; }
    public string? ReferenceNumber { get; init; }
}

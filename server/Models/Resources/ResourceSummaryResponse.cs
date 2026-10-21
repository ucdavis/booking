namespace Server.Models.Resources;

public class ResourceSummaryResponse
{
    public int Id { get; init; }
    public int TeamId { get; init; }
    public int SpaceId { get; init; }
    public string SpaceName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int? Capacity { get; init; }
    public bool IsActive { get; init; }
    public bool IsReservable { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int? ParentResourceId { get; init; }
}

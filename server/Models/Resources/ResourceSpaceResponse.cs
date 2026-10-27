namespace Server.Models.Resources;

public sealed class ResourceSpaceResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Address { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
}

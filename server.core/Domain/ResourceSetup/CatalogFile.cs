using Server.Core.Domain.Administration;

namespace Server.Core.Domain.ResourceSetup;

public class CatalogFile
{
    public int Id { get; set; }
    public int? SpaceId { get; set; }
    public Space? Space { get; set; }
    public int? ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public required string StorageKey { get; set; }
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

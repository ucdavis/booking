using Server.Core.Domain.Administration;

namespace Server.Core.Domain.ResourceSetup;

public class ResourceConfig
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public int Version { get; set; }
    public int? SourceTemplateId { get; set; }
    public ResourceTemplate? SourceTemplate { get; set; }
    public int FormSchemaVersion { get; set; }
    public required string FormJson { get; set; }
    public int DetailsSchemaVersion { get; set; } = 1;

    // Versioned hours and billing settings; never expose this entire document publicly.
    public required string DetailsJson { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? PublishedByUserId { get; set; }
    public User? PublishedByUser { get; set; }

    // Published versions are immutable; edits require a new draft and publication.
    public DateTimeOffset? PublishedAt { get; set; }
}

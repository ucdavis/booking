using Server.Core.Domain.Administration;

namespace Server.Core.Domain.ResourceSetup;

public class ResourceTemplate
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public required string Name { get; set; }
    public int FormSchemaVersion { get; set; }
    public required string FormJson { get; set; }

    // Copy-once defaults; the destination team supplies its own billing account.
    public string? ResourceDefaultsJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public User UpdatedByUser { get; set; } = null!;
}

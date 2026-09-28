using Server.Core.Domain.Administration;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Domain.CalendarPublication;

/// <summary>
/// Publishes a fixed calendar view. Team and scope are immutable after creation;
/// feed tokens must not be included in ordinary catalog responses.
/// </summary>
public class CalendarFeed
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int? TeamId { get; set; }

    public Team? Team { get; set; }

    public int? SpaceId { get; set; }

    public Space? Space { get; set; }

    public int? ResourceId { get; set; }

    public Resource? Resource { get; set; }

    public string DisplayMode { get; set; } = "availability";

    public Guid ShareToken { get; set; } = Guid.NewGuid();

    public DateTimeOffset? DisabledAt { get; set; }

    public int CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public int UpdatedByUserId { get; set; }

    public User UpdatedByUser { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

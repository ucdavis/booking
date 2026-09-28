using Server.Core.Domain.Administration;

namespace Server.Core.Domain.ResourceSetup;

public class Resource
{
    public int Id { get; set; }
    public int SpaceId { get; set; }
    public Space Space { get; set; } = null!;
    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public int? ParentResourceId { get; set; }
    public Resource? ParentResource { get; set; }
    public required string Category { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }

    // Normalize from Name server-side; staff do not enter the duplicate key.
    public required string NameKey { get; set; }
    public string? Description { get; set; }
    public int? Capacity { get; set; }
    public bool IsReservable { get; set; } = true;
    public string ApprovalMode { get; set; } = "manual";
    public bool FollowTeamCalendar { get; set; } = true;

    // Root default only. Children must store null and inherit the root's policy.
    public string? PublicAccess { get; set; } = "availability";
    public bool AllowReservationSharing { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public User UpdatedByUser { get; set; } = null!;
}

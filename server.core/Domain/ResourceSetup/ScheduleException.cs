using Server.Core.Domain.Administration;

namespace Server.Core.Domain.ResourceSetup;

public class ScheduleException
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public int? ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public DateOnly LocalDate { get; set; }
    public required string Kind { get; set; }
    public string? IntervalsJson { get; set; }
    public required string Label { get; set; }
    public string Source { get; set; } = "manual";
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

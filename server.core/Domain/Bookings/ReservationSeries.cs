using Server.Core.Domain.Administration;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Domain.Bookings;

public class ReservationSeries
{
    public int Id { get; set; }

    public int ResourceId { get; set; }

    public Resource Resource { get; set; } = null!;

    public int RequesterUserId { get; set; }

    public User RequesterUser { get; set; } = null!;

    public int ResourceConfigId { get; set; }

    public ResourceConfig ResourceConfig { get; set; } = null!;

    public string? Title { get; set; }

    public required string FormResponsesJson { get; set; }

    public required string TimeZoneId { get; set; }

    public string? RecurrenceJson { get; set; }

    public Guid? ShareToken { get; set; }

    public Guid SubmissionKey { get; set; }

    public int CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}

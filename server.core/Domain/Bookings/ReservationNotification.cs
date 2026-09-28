using Server.Core.Domain.Administration;

namespace Server.Core.Domain.Bookings;

public class ReservationNotification
{
    public int Id { get; set; }

    public int ReservationSeriesId { get; set; }

    public ReservationSeries ReservationSeries { get; set; } = null!;

    public int? ReservationId { get; set; }

    public Reservation? Reservation { get; set; }

    public int RecipientUserId { get; set; }

    public User RecipientUser { get; set; } = null!;

    public required string Kind { get; set; }

    public required string DeduplicationKey { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}

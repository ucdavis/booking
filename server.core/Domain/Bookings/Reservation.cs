using Server.Core.Domain.Administration;

namespace Server.Core.Domain.Bookings;

public class Reservation
{
    public int Id { get; set; }

    public int ReservationSeriesId { get; set; }

    public ReservationSeries ReservationSeries { get; set; } = null!;

    public int OccurrenceNumber { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public string Status { get; set; } = "pending";

    public int? DecidedByUserId { get; set; }

    public User? DecidedByUser { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    public Guid? ShareToken { get; set; }

    public DateTimeOffset BillingDueAt { get; set; }

    public string BillingStatus { get; set; } = "waiting";

    public DateTimeOffset? BillingPreparedAt { get; set; }

    public decimal? Amount { get; set; }

    public string? BillingSnapshotJson { get; set; }

    public DateTimeOffset? BillingDispatchStartedAt { get; set; }

    public int? PaymentsInvoiceId { get; set; }

    public string? PaymentsLinkId { get; set; }

    public string? PaymentsStatus { get; set; }

    public DateTimeOffset? PaymentsSyncedAt { get; set; }

    public bool BillingAttentionRequired { get; set; }

    public string? BillingAttentionNote { get; set; }

    public int Revision { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

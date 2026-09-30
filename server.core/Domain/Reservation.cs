using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("Reservations")]
public class Reservation
{
    [Key]
    public int Id { get; set; }

    public int ReservationSeriesId { get; set; }

    public ReservationSeries ReservationSeries { get; set; } = null!;

    public int OccurrenceNumber { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public int? DecidedByUserId { get; set; }

    public User? DecidedByUser { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    [MaxLength(1000)]
    public string? DecisionNote { get; set; }

    public Guid? ShareToken { get; set; }

    public DateTimeOffset BillingDueAt { get; set; }

    [Required]
    [MaxLength(24)]
    public string BillingStatus { get; set; } = "waiting";

    public DateTimeOffset? BillingPreparedAt { get; set; }

    [Precision(12, 2)]
    public decimal? Amount { get; set; }

    public string? BillingSnapshotJson { get; set; }

    public DateTimeOffset? BillingDispatchStartedAt { get; set; }

    public int? PaymentsInvoiceId { get; set; }

    [MaxLength(128)]
    public string? PaymentsLinkId { get; set; }

    [MaxLength(50)]
    public string? PaymentsStatus { get; set; }

    public DateTimeOffset? PaymentsSyncedAt { get; set; }

    public bool BillingAttentionRequired { get; set; }

    [MaxLength(2000)]
    public string? BillingAttentionNote { get; set; }

    [ConcurrencyCheck]
    public int Revision { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Reservation>();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Reservations_Interval", "[OccurrenceNumber] > 0 AND [Revision] > 0 AND [EndsAt] > [StartsAt]");
            table.HasCheckConstraint("CK_Reservations_Status", "[Status] IN ('pending', 'approved', 'rejected', 'canceled')");
            table.HasCheckConstraint("CK_Reservations_BillingStatus", "[BillingStatus] IN ('waiting', 'prepared', 'submitting', 'linked', 'free', 'skipped', 'needs_attention')");
            table.HasCheckConstraint("CK_Reservations_Calculation", "([BillingPreparedAt] IS NULL AND [Amount] IS NULL AND [BillingSnapshotJson] IS NULL) OR ([BillingPreparedAt] IS NOT NULL AND [Amount] IS NOT NULL AND [Amount] >= 0 AND [BillingSnapshotJson] IS NOT NULL AND ISJSON([BillingSnapshotJson]) = 1)");
            table.HasCheckConstraint("CK_Reservations_Unprepared", "[BillingStatus] NOT IN ('waiting', 'skipped') OR ([BillingPreparedAt] IS NULL AND [BillingDispatchStartedAt] IS NULL AND [PaymentsInvoiceId] IS NULL)");
            table.HasCheckConstraint("CK_Reservations_Free", "[BillingStatus] <> 'free' OR ([BillingPreparedAt] IS NOT NULL AND [Amount] = 0 AND [BillingDispatchStartedAt] IS NULL AND [PaymentsInvoiceId] IS NULL)");
            table.HasCheckConstraint("CK_Reservations_Prepared", "[BillingStatus] <> 'prepared' OR ([BillingPreparedAt] IS NOT NULL AND [Amount] > 0 AND [BillingDispatchStartedAt] IS NULL AND [PaymentsInvoiceId] IS NULL)");
            table.HasCheckConstraint("CK_Reservations_Dispatched", "[BillingDispatchStartedAt] IS NULL OR ([BillingPreparedAt] IS NOT NULL AND [Amount] > 0)");
            table.HasCheckConstraint("CK_Reservations_Submitting", "[BillingStatus] NOT IN ('submitting', 'linked') OR [BillingDispatchStartedAt] IS NOT NULL");
            table.HasCheckConstraint("CK_Reservations_ExternalInvoice", "[PaymentsInvoiceId] IS NULL OR [BillingDispatchStartedAt] IS NOT NULL");
            table.HasCheckConstraint("CK_Reservations_Linked", "[BillingStatus] <> 'linked' OR ([PaymentsInvoiceId] IS NOT NULL AND [PaymentsLinkId] IS NOT NULL)");
            table.HasCheckConstraint("CK_Reservations_Attention", "[BillingStatus] <> 'needs_attention' OR ([BillingAttentionRequired] = 1 AND [BillingAttentionNote] IS NOT NULL)");
        });

        builder.HasAlternateKey(reservation => new { reservation.Id, reservation.ReservationSeriesId });
        builder.Property(reservation => reservation.Id).UseIdentityColumn();
        builder.Property(reservation => reservation.Status).HasDefaultValue("pending");
        builder.Property(reservation => reservation.BillingStatus).HasDefaultValue("waiting");
        builder.Property(reservation => reservation.BillingAttentionRequired).HasDefaultValue(false);
        builder.Property(reservation => reservation.Revision).HasDefaultValue(1);

        builder.HasIndex(reservation => new { reservation.ReservationSeriesId, reservation.OccurrenceNumber }).IsUnique();
        builder.HasIndex(reservation => new { reservation.Status, reservation.StartsAt });
        builder.HasIndex(reservation => new { reservation.BillingStatus, reservation.BillingDueAt });
        builder.HasIndex(reservation => reservation.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
        builder.HasIndex(reservation => reservation.PaymentsInvoiceId).IsUnique().HasFilter("[PaymentsInvoiceId] IS NOT NULL");

        builder.HasOne(reservation => reservation.ReservationSeries)
            .WithMany()
            .HasForeignKey(reservation => reservation.ReservationSeriesId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(reservation => reservation.DecidedByUser)
            .WithMany()
            .HasForeignKey(reservation => reservation.DecidedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

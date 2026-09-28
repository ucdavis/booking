using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Bookings;

namespace Server.Core.Data.Configurations.Bookings;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", table =>
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

        builder.HasKey(reservation => reservation.Id);
        builder.HasAlternateKey(reservation => new { reservation.Id, reservation.ReservationSeriesId });
        builder.Property(reservation => reservation.Id).UseIdentityColumn();
        builder.Property(reservation => reservation.Status).HasMaxLength(20).IsRequired().HasDefaultValue("pending");
        builder.Property(reservation => reservation.DecisionNote).HasMaxLength(1000);
        builder.Property(reservation => reservation.BillingStatus).HasMaxLength(24).IsRequired().HasDefaultValue("waiting");
        builder.Property(reservation => reservation.Amount).HasPrecision(12, 2);
        builder.Property(reservation => reservation.PaymentsLinkId).HasMaxLength(128);
        builder.Property(reservation => reservation.PaymentsStatus).HasMaxLength(50);
        builder.Property(reservation => reservation.BillingAttentionRequired).HasDefaultValue(false);
        builder.Property(reservation => reservation.BillingAttentionNote).HasMaxLength(2000);
        builder.Property(reservation => reservation.Revision).HasDefaultValue(1).IsConcurrencyToken();

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

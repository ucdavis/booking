using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Bookings;

namespace Server.Core.Data.Configurations.Bookings;

public class ReservationNotificationConfiguration : IEntityTypeConfiguration<ReservationNotification>
{
    public void Configure(EntityTypeBuilder<ReservationNotification> builder)
    {
        builder.ToTable("Notifications", table =>
        {
            table.HasCheckConstraint("CK_Notifications_Payload", "[AttemptCount] >= 0 AND ISJSON([PayloadJson]) = 1");
        });

        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).UseIdentityColumn();
        builder.Property(notification => notification.Kind).HasMaxLength(50).IsRequired();
        builder.Property(notification => notification.DeduplicationKey).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.PayloadJson).IsRequired();
        builder.Property(notification => notification.AttemptCount).HasDefaultValue(0);
        builder.Property(notification => notification.LastError).HasMaxLength(2000);

        builder.HasIndex(notification => notification.DeduplicationKey).IsUnique();
        builder.HasIndex(notification => new { notification.SentAt, notification.NextAttemptAt });

        builder.HasOne(notification => notification.ReservationSeries)
            .WithMany()
            .HasForeignKey(notification => notification.ReservationSeriesId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(notification => notification.Reservation)
            .WithMany()
            .HasForeignKey(notification => new { notification.ReservationId, notification.ReservationSeriesId })
            .HasPrincipalKey(reservation => new { reservation.Id, reservation.ReservationSeriesId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(notification => notification.RecipientUser)
            .WithMany()
            .HasForeignKey(notification => notification.RecipientUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

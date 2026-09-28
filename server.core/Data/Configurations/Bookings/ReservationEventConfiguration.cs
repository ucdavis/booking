using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Bookings;

namespace Server.Core.Data.Configurations.Bookings;

public class ReservationEventConfiguration : IEntityTypeConfiguration<ReservationEvent>
{
    public void Configure(EntityTypeBuilder<ReservationEvent> builder)
    {
        builder.ToTable("ReservationEvents", table =>
        {
            table.HasCheckConstraint("CK_ReservationEvents_Details", "[ReservationRevision] > 0 AND ISJSON([DetailsJson]) = 1");
        });

        builder.HasKey(reservationEvent => reservationEvent.Id);
        builder.Property(reservationEvent => reservationEvent.Id).UseIdentityColumn();
        builder.Property(reservationEvent => reservationEvent.EventType).HasMaxLength(60).IsRequired();
        builder.Property(reservationEvent => reservationEvent.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(reservationEvent => reservationEvent.DetailsJson).IsRequired();

        builder.HasIndex(reservationEvent => new { reservationEvent.ReservationId, reservationEvent.ReservationRevision }).IsUnique();

        builder.HasOne(reservationEvent => reservationEvent.Reservation)
            .WithMany()
            .HasForeignKey(reservationEvent => reservationEvent.ReservationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(reservationEvent => reservationEvent.ActorUser)
            .WithMany()
            .HasForeignKey(reservationEvent => reservationEvent.ActorUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

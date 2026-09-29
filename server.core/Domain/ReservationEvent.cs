using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class ReservationEvent
{
    public int Id { get; set; }

    public int ReservationId { get; set; }

    public Reservation Reservation { get; set; } = null!;

    public int ReservationRevision { get; set; }

    public required string EventType { get; set; }

    public int? ActorUserId { get; set; }

    public User? ActorUser { get; set; }

    public required string ActorName { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string DetailsJson { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ReservationEvent>();

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

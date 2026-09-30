using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("ReservationEvents")]
public class ReservationEvent
{
    [Key]
    public int Id { get; set; }

    public int ReservationId { get; set; }

    public Reservation Reservation { get; set; } = null!;

    public int ReservationRevision { get; set; }

    [Required]
    [MaxLength(60)]
    public required string EventType { get; set; }

    public int? ActorUserId { get; set; }

    public User? ActorUser { get; set; }

    [Required]
    [MaxLength(200)]
    public required string ActorName { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    [Required]
    public required string DetailsJson { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ReservationEvent>();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ReservationEvents_Details", "[ReservationRevision] > 0 AND ISJSON([DetailsJson]) = 1");
        });

        builder.Property(reservationEvent => reservationEvent.Id).UseIdentityColumn();

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

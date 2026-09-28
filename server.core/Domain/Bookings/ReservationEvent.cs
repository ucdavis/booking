using Server.Core.Domain.Administration;

namespace Server.Core.Domain.Bookings;

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
}

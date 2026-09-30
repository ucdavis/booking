using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("Notifications")]
public class ReservationNotification
{
    [Key]
    public int Id { get; set; }

    public int ReservationSeriesId { get; set; }

    public ReservationSeries ReservationSeries { get; set; } = null!;

    public int? ReservationId { get; set; }

    public Reservation? Reservation { get; set; }

    public int RecipientUserId { get; set; }

    public User RecipientUser { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public required string Kind { get; set; }

    [Required]
    [MaxLength(200)]
    public required string DeduplicationKey { get; set; }

    [Required]
    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ReservationNotification>();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Notifications_Payload", "[AttemptCount] >= 0 AND ISJSON([PayloadJson]) = 1");
        });

        builder.Property(notification => notification.Id).UseIdentityColumn();
        builder.Property(notification => notification.AttemptCount).HasDefaultValue(0);

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

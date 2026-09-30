using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("ReservationSeries")]
public class ReservationSeries
{
    [Key]
    public int Id { get; set; }

    public int ResourceId { get; set; }

    public Resource Resource { get; set; } = null!;

    public int RequesterUserId { get; set; }

    public User RequesterUser { get; set; } = null!;

    public int ResourceConfigId { get; set; }

    public ResourceConfig ResourceConfig { get; set; } = null!;

    [MaxLength(200)]
    public string? Title { get; set; }

    [Required]
    public required string FormResponsesJson { get; set; }

    [Required]
    [MaxLength(100)]
    public required string TimeZoneId { get; set; }

    public string? RecurrenceJson { get; set; }

    public Guid? ShareToken { get; set; }

    public Guid SubmissionKey { get; set; }

    public int CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ReservationSeries>();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ReservationSeries_Responses", "ISJSON([FormResponsesJson]) = 1");
            table.HasCheckConstraint("CK_ReservationSeries_Recurrence", "[RecurrenceJson] IS NULL OR ISJSON([RecurrenceJson]) = 1");
        });

        builder.Property(series => series.Id).UseIdentityColumn();

        builder.HasIndex(series => series.SubmissionKey).IsUnique();
        builder.HasIndex(series => series.ShareToken).IsUnique().HasFilter("[ShareToken] IS NOT NULL");
        builder.HasIndex(series => new { series.ResourceId, series.CreatedAt });
        builder.HasIndex(series => new { series.RequesterUserId, series.CreatedAt });

        builder.HasOne(series => series.Resource)
            .WithMany()
            .HasForeignKey(series => series.ResourceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(series => series.RequesterUser)
            .WithMany()
            .HasForeignKey(series => series.RequesterUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(series => series.ResourceConfig)
            .WithMany()
            .HasForeignKey(series => new { series.ResourceConfigId, series.ResourceId })
            .HasPrincipalKey(config => new { config.Id, config.ResourceId })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(series => series.CreatedByUser)
            .WithMany()
            .HasForeignKey(series => series.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class ScheduleException
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public int? ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public DateOnly LocalDate { get; set; }
    public required string Kind { get; set; }
    public string? IntervalsJson { get; set; }
    public required string Label { get; set; }
    public string Source { get; set; } = "manual";
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ScheduleException>();
        builder.ToTable("ScheduleExceptions", table =>
        {
            table.HasCheckConstraint("CK_ScheduleExceptions_Scope", "(TeamId IS NOT NULL AND ResourceId IS NULL) OR (TeamId IS NULL AND ResourceId IS NOT NULL)");
            table.HasCheckConstraint("CK_ScheduleExceptions_Kind", "Kind IN ('closed', 'hours') AND Source IN ('manual', 'holiday_import')");
            table.HasCheckConstraint("CK_ScheduleExceptions_Intervals", "(Kind = 'closed' AND IntervalsJson IS NULL) OR (Kind = 'hours' AND ResourceId IS NOT NULL AND IntervalsJson IS NOT NULL AND ISJSON(IntervalsJson) = 1)");
        });

        builder.HasKey(exception => exception.Id);
        builder.Property(exception => exception.Id).UseIdentityColumn();
        builder.Property(exception => exception.LocalDate).HasColumnType("date");
        builder.Property(exception => exception.Kind).HasMaxLength(20).IsRequired();
        builder.Property(exception => exception.IntervalsJson).HasColumnType("nvarchar(max)");
        builder.Property(exception => exception.Label).HasMaxLength(200).IsRequired();
        builder.Property(exception => exception.Source).HasMaxLength(20).IsRequired().HasDefaultValue("manual");
        builder.HasIndex(exception => new { exception.TeamId, exception.LocalDate })
            .IsUnique()
            .HasFilter("[TeamId] IS NOT NULL");
        builder.HasIndex(exception => new { exception.ResourceId, exception.LocalDate })
            .IsUnique()
            .HasFilter("[ResourceId] IS NOT NULL");

        builder.HasOne(exception => exception.Team)
            .WithMany()
            .HasForeignKey(exception => exception.TeamId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(exception => exception.Resource)
            .WithMany()
            .HasForeignKey(exception => exception.ResourceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(exception => exception.CreatedByUser)
            .WithMany()
            .HasForeignKey(exception => exception.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Data.Configurations.ResourceSetup;

public class ScheduleExceptionConfiguration : IEntityTypeConfiguration<ScheduleException>
{
    public void Configure(EntityTypeBuilder<ScheduleException> builder)
    {
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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Bookings;

namespace Server.Core.Data.Configurations.Bookings;

public class ReservationSeriesConfiguration : IEntityTypeConfiguration<ReservationSeries>
{
    public void Configure(EntityTypeBuilder<ReservationSeries> builder)
    {
        builder.ToTable("ReservationSeries", table =>
        {
            table.HasCheckConstraint("CK_ReservationSeries_Responses", "ISJSON([FormResponsesJson]) = 1");
            table.HasCheckConstraint("CK_ReservationSeries_Recurrence", "[RecurrenceJson] IS NULL OR ISJSON([RecurrenceJson]) = 1");
        });

        builder.HasKey(series => series.Id);
        builder.Property(series => series.Id).UseIdentityColumn();
        builder.Property(series => series.Title).HasMaxLength(200);
        builder.Property(series => series.FormResponsesJson).IsRequired();
        builder.Property(series => series.TimeZoneId).HasMaxLength(100).IsRequired();

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

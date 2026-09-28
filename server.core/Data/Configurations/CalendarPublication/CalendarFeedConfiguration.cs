using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.CalendarPublication;

namespace Server.Core.Data.Configurations.CalendarPublication;

public class CalendarFeedConfiguration : IEntityTypeConfiguration<CalendarFeed>
{
    public void Configure(EntityTypeBuilder<CalendarFeed> builder)
    {
        builder.ToTable("CalendarFeeds", table =>
        {
            table.HasCheckConstraint("CK_CalendarFeeds_Scope",
                "([SpaceId] IS NOT NULL AND [ResourceId] IS NULL) OR ([SpaceId] IS NULL AND [ResourceId] IS NOT NULL)");
            table.HasCheckConstraint("CK_CalendarFeeds_ResourceTeam",
                "[ResourceId] IS NULL OR [TeamId] IS NOT NULL");
            table.HasCheckConstraint("CK_CalendarFeeds_Display",
                "[DisplayMode] IN ('availability', 'titles', 'details')");
        });

        builder.HasKey(feed => feed.Id);
        builder.Property(feed => feed.Id).UseIdentityColumn();
        builder.Property(feed => feed.Name).HasMaxLength(200).IsRequired();
        builder.Property(feed => feed.DisplayMode).HasMaxLength(20).IsRequired()
            .HasDefaultValue("availability");
        builder.HasIndex(feed => feed.ShareToken).IsUnique();

        builder.HasOne(feed => feed.Team).WithMany()
            .HasForeignKey(feed => feed.TeamId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(feed => feed.Space).WithMany()
            .HasForeignKey(feed => feed.SpaceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(feed => feed.Resource).WithMany()
            .HasForeignKey(feed => feed.ResourceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(feed => feed.CreatedByUser).WithMany()
            .HasForeignKey(feed => feed.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(feed => feed.UpdatedByUser).WithMany()
            .HasForeignKey(feed => feed.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

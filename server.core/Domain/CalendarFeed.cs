using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

/// <summary>
/// Publishes a fixed calendar view. Team and scope are immutable after creation;
/// feed tokens must not be included in ordinary catalog responses.
/// </summary>
public class CalendarFeed
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int? TeamId { get; set; }

    public Team? Team { get; set; }

    public int? SpaceId { get; set; }

    public Space? Space { get; set; }

    public int? ResourceId { get; set; }

    public Resource? Resource { get; set; }

    public string DisplayMode { get; set; } = "availability";

    public Guid ShareToken { get; set; } = Guid.NewGuid();

    public DateTimeOffset? DisabledAt { get; set; }

    public int CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public int UpdatedByUserId { get; set; }

    public User UpdatedByUser { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<CalendarFeed>();

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

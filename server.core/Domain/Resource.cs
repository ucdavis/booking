using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class Resource
{
    public int Id { get; set; }
    public int SpaceId { get; set; }
    public Space Space { get; set; } = null!;
    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public int? ParentResourceId { get; set; }
    public Resource? ParentResource { get; set; }
    public required string Category { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }

    // Normalize from Name server-side; staff do not enter the duplicate key.
    public required string NameKey { get; set; }
    public string? Description { get; set; }
    public int? Capacity { get; set; }
    public bool IsReservable { get; set; } = true;
    public string ApprovalMode { get; set; } = "manual";
    public bool FollowTeamCalendar { get; set; } = true;

    // Root default only. Children must store null and inherit the root's policy.
    public string? PublicAccess { get; set; } = "availability";
    public bool AllowReservationSharing { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public User UpdatedByUser { get; set; } = null!;

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Resource>();
        builder.ToTable("Resources", table =>
        {
            table.HasCheckConstraint("CK_Resources_Capacity", "Capacity IS NULL OR Capacity > 0");
            table.HasCheckConstraint("CK_Resources_Approval", "ApprovalMode IN ('automatic', 'manual')");
            table.HasCheckConstraint("CK_Resources_PublicScope", "(ParentResourceId IS NULL AND PublicAccess IS NOT NULL AND PublicAccess IN ('none', 'availability', 'details')) OR (ParentResourceId IS NOT NULL AND PublicAccess IS NULL)");
        });

        builder.HasKey(resource => resource.Id);
        builder.Property(resource => resource.Id).UseIdentityColumn();
        builder.HasAlternateKey(resource => new { resource.Id, resource.SpaceId, resource.TeamId });
        builder.Property(resource => resource.Category).HasMaxLength(50).IsRequired();
        builder.Property(resource => resource.Slug).HasMaxLength(120).IsRequired();
        builder.Property(resource => resource.Name).HasMaxLength(200).IsRequired();
        builder.Property(resource => resource.NameKey).HasMaxLength(200).IsRequired();
        builder.Property(resource => resource.Description).HasColumnType("nvarchar(max)");
        builder.Property(resource => resource.IsReservable).HasDefaultValue(true);
        builder.Property(resource => resource.ApprovalMode).HasMaxLength(20).IsRequired().HasDefaultValue("manual");
        builder.Property(resource => resource.FollowTeamCalendar).HasDefaultValue(true);
        builder.Property(resource => resource.PublicAccess).HasMaxLength(20);
        builder.Property(resource => resource.AllowReservationSharing).HasDefaultValue(false);
        builder.Property(resource => resource.IsActive).HasDefaultValue(true);

        builder.HasIndex(resource => new { resource.SpaceId, resource.Slug }).IsUnique();
        builder.HasIndex(resource => new { resource.SpaceId, resource.ParentResourceId, resource.NameKey })
            .IsUnique()
            .HasFilter(null);
        builder.HasIndex(resource => new { resource.TeamId, resource.SpaceId });
        builder.HasIndex(resource => resource.ParentResourceId);

        builder.HasOne(resource => resource.Space)
            .WithMany()
            .HasForeignKey(resource => resource.SpaceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(resource => resource.Team)
            .WithMany()
            .HasForeignKey(resource => resource.TeamId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(resource => resource.ParentResource)
            .WithMany()
            .HasForeignKey(resource => new { resource.ParentResourceId, resource.SpaceId, resource.TeamId })
            .HasPrincipalKey(resource => new { resource.Id, resource.SpaceId, resource.TeamId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(resource => resource.UpdatedByUser)
            .WithMany()
            .HasForeignKey(resource => resource.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

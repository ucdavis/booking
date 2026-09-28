using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Data.Configurations.ResourceSetup;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
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

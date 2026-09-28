using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Administration;

namespace Server.Core.Data.Configurations.Administration;

public class TeamPermissionConfiguration : IEntityTypeConfiguration<TeamPermission>
{
    public void Configure(EntityTypeBuilder<TeamPermission> builder)
    {
        builder.ToTable("TeamPermissions", table => table.HasCheckConstraint(
            "CK_TeamPermissions_Role", "[Role] IN ('admin', 'editor', 'viewer')"));
        builder.HasKey(permission => new { permission.TeamId, permission.UserId });
        builder.Property(permission => permission.Role).HasMaxLength(20).IsRequired();

        builder.HasOne(permission => permission.User)
            .WithMany()
            .HasForeignKey(permission => permission.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(permission => permission.Team)
            .WithMany()
            .HasForeignKey(permission => permission.TeamId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

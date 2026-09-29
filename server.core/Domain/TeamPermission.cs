using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class TeamPermission
{
    public int UserId { get; set; }

    public int TeamId { get; set; }

    public required string Role { get; set; }

    public User User { get; set; } = null!;

    public Team Team { get; set; } = null!;

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<TeamPermission>();

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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("TeamPermissions")]
public class TeamPermission
{
    public int UserId { get; set; }

    public int TeamId { get; set; }

    [Required]
    [MaxLength(20)]
    public required string Role { get; set; }

    public User User { get; set; } = null!;

    public Team Team { get; set; } = null!;

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<TeamPermission>();

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_TeamPermissions_Role", "[Role] IN ('admin', 'editor', 'viewer')"));
        builder.HasKey(permission => new { permission.TeamId, permission.UserId });

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

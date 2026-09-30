using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("TeamSpaces")]
public class TeamSpace
{
    public int TeamId { get; set; }

    public int SpaceId { get; set; }

    public Team Team { get; set; } = null!;

    public Space Space { get; set; } = null!;

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<TeamSpace>();

        builder.HasKey(teamSpace => new { teamSpace.TeamId, teamSpace.SpaceId });

        builder.HasOne(teamSpace => teamSpace.Team)
            .WithMany()
            .HasForeignKey(teamSpace => teamSpace.TeamId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(teamSpace => teamSpace.Space)
            .WithMany()
            .HasForeignKey(teamSpace => teamSpace.SpaceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

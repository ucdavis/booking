using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Administration;

namespace Server.Core.Data.Configurations.Administration;

public class TeamSpaceConfiguration : IEntityTypeConfiguration<TeamSpace>
{
    public void Configure(EntityTypeBuilder<TeamSpace> builder)
    {
        builder.ToTable("TeamSpaces");
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

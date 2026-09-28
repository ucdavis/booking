using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Administration;

namespace Server.Core.Data.Configurations.Administration;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.HasKey(team => team.Id);
        builder.Property(team => team.Id).ValueGeneratedOnAdd();
        builder.Property(team => team.Name).HasMaxLength(200).IsRequired();
        builder.Property(team => team.Slug).HasMaxLength(100).IsRequired();
        builder.Property(team => team.PaymentsTeamSlug).HasMaxLength(100);
        builder.Property(team => team.PaymentsApiKeySecretName).HasMaxLength(200);

        builder.HasIndex(team => team.Slug).IsUnique();
    }
}

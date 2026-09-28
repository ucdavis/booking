using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Data.Configurations.ResourceSetup;

public class ResourceConfigConfiguration : IEntityTypeConfiguration<ResourceConfig>
{
    public void Configure(EntityTypeBuilder<ResourceConfig> builder)
    {
        builder.ToTable("ResourceConfigs", table =>
        {
            table.HasCheckConstraint("CK_ResourceConfigs_Form", "Version > 0 AND FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
            table.HasCheckConstraint("CK_ResourceConfigs_Details", "DetailsSchemaVersion > 0 AND ISJSON(DetailsJson) = 1");
            table.HasCheckConstraint("CK_ResourceConfigs_Publication", "(PublishedAt IS NULL AND PublishedByUserId IS NULL) OR (PublishedAt IS NOT NULL AND PublishedByUserId IS NOT NULL)");
        });

        builder.HasKey(config => config.Id);
        builder.Property(config => config.Id).UseIdentityColumn();
        builder.HasAlternateKey(config => new { config.Id, config.ResourceId });
        builder.HasIndex(config => new { config.ResourceId, config.Version }).IsUnique();
        builder.Property(config => config.FormJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(config => config.DetailsSchemaVersion).HasDefaultValue(1);
        builder.Property(config => config.DetailsJson).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasOne(config => config.Resource)
            .WithMany()
            .HasForeignKey(config => config.ResourceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(config => config.SourceTemplate)
            .WithMany()
            .HasForeignKey(config => config.SourceTemplateId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(config => config.CreatedByUser)
            .WithMany()
            .HasForeignKey(config => config.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(config => config.PublishedByUser)
            .WithMany()
            .HasForeignKey(config => config.PublishedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

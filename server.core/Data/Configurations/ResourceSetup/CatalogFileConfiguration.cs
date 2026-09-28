using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Data.Configurations.ResourceSetup;

public class CatalogFileConfiguration : IEntityTypeConfiguration<CatalogFile>
{
    public void Configure(EntityTypeBuilder<CatalogFile> builder)
    {
        builder.ToTable("Files", table =>
        {
            table.HasCheckConstraint("CK_Files_OneOwner", "(SpaceId IS NOT NULL AND ResourceId IS NULL) OR (SpaceId IS NULL AND ResourceId IS NOT NULL)");
            table.HasCheckConstraint("CK_Files_Size", "SizeBytes >= 0");
        });

        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).UseIdentityColumn();
        builder.Property(file => file.StorageKey).HasMaxLength(500).IsRequired();
        builder.HasIndex(file => file.StorageKey).IsUnique();
        builder.Property(file => file.Name).HasMaxLength(255).IsRequired();
        builder.Property(file => file.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(file => file.AltText).HasMaxLength(500);
        builder.Property(file => file.SortOrder).HasDefaultValue(0);

        builder.HasOne(file => file.Space)
            .WithMany()
            .HasForeignKey(file => file.SpaceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(file => file.Resource)
            .WithMany()
            .HasForeignKey(file => file.ResourceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(file => file.CreatedByUser)
            .WithMany()
            .HasForeignKey(file => file.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

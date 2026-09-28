using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Server.Core.Domain.Administration;

namespace Server.Core.Data.Configurations.Administration;

public class SpaceConfiguration : IEntityTypeConfiguration<Space>
{
    public void Configure(EntityTypeBuilder<Space> builder)
    {
        builder.ToTable("Spaces", table => table.HasCheckConstraint(
            "CK_Spaces_OfficialReference", "[IsOfficialFacility] = 0 OR [ReferenceNumber] IS NOT NULL"));
        builder.HasKey(space => space.Id);
        builder.Property(space => space.Id).ValueGeneratedOnAdd();
        builder.Property(space => space.Slug).HasMaxLength(120).IsRequired();
        builder.Property(space => space.Name).HasMaxLength(200).IsRequired();
        builder.Property(space => space.Description).HasColumnType("nvarchar(max)");
        builder.Property(space => space.Address).HasMaxLength(500);
        builder.Property(space => space.IsOfficialFacility).HasDefaultValue(false);
        builder.Property(space => space.ReferenceNumber).HasMaxLength(100);
        builder.Property(space => space.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(space => space.IsActive).HasDefaultValue(true);

        builder.HasIndex(space => space.Slug).IsUnique();
        builder.HasIndex(space => space.ReferenceNumber)
            .IsUnique()
            .HasFilter("[IsOfficialFacility] = 1 AND [ReferenceNumber] IS NOT NULL");
    }
}

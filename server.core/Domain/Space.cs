using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class Space
{
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Address { get; set; }

    public bool IsOfficialFacility { get; set; }

    public string? ReferenceNumber { get; set; }

    public required string TimeZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Space>();

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

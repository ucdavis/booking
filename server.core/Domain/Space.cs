using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("Spaces")]
public class Space
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(120)]
    public required string Slug { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public bool IsOfficialFacility { get; set; }

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    [Required]
    [MaxLength(100)]
    public required string TimeZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Space>();

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Spaces_OfficialReference", "[IsOfficialFacility] = 0 OR [ReferenceNumber] IS NOT NULL"));
        builder.Property(space => space.IsOfficialFacility).HasDefaultValue(false);
        builder.Property(space => space.IsActive).HasDefaultValue(true);

        builder.HasIndex(space => space.Slug).IsUnique();
        builder.HasIndex(space => space.ReferenceNumber)
            .IsUnique()
            .HasFilter("[IsOfficialFacility] = 1 AND [ReferenceNumber] IS NOT NULL");
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("ResourceConfigs")]
public class ResourceConfig
{
    [Key]
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public int Version { get; set; }
    public int? SourceTemplateId { get; set; }
    public ResourceTemplate? SourceTemplate { get; set; }
    public int FormSchemaVersion { get; set; }
    [Required]
    [Column(TypeName = "nvarchar(max)")]
    public required string FormJson { get; set; }
    public int DetailsSchemaVersion { get; set; } = 1;

    // Versioned hours and billing settings; never expose this entire document publicly.
    [Required]
    [Column(TypeName = "nvarchar(max)")]
    public required string DetailsJson { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? PublishedByUserId { get; set; }
    public User? PublishedByUser { get; set; }

    // Published versions are immutable; edits require a new draft and publication.
    public DateTimeOffset? PublishedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ResourceConfig>();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ResourceConfigs_Form", "Version > 0 AND FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
            table.HasCheckConstraint("CK_ResourceConfigs_Details", "DetailsSchemaVersion > 0 AND ISJSON(DetailsJson) = 1");
            table.HasCheckConstraint("CK_ResourceConfigs_Publication", "(PublishedAt IS NULL AND PublishedByUserId IS NULL) OR (PublishedAt IS NOT NULL AND PublishedByUserId IS NOT NULL)");
        });

        builder.Property(config => config.Id).UseIdentityColumn();
        builder.HasAlternateKey(config => new { config.Id, config.ResourceId });
        builder.HasIndex(config => new { config.ResourceId, config.Version }).IsUnique();
        builder.Property(config => config.DetailsSchemaVersion).HasDefaultValue(1);

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

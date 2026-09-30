using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("ResourceTemplates")]
public class ResourceTemplate
{
    [Key]
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }
    public int FormSchemaVersion { get; set; }
    [Required]
    [Column(TypeName = "nvarchar(max)")]
    public required string FormJson { get; set; }

    // Copy-once defaults; the destination team supplies its own billing account.
    [Column(TypeName = "nvarchar(max)")]
    public string? ResourceDefaultsJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public User UpdatedByUser { get; set; } = null!;

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ResourceTemplate>();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ResourceTemplates_Form", "FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
            table.HasCheckConstraint("CK_ResourceTemplates_Defaults", "ResourceDefaultsJson IS NULL OR ISJSON(ResourceDefaultsJson) = 1");
        });

        builder.Property(template => template.Id).UseIdentityColumn();
        builder.Property(template => template.IsActive).HasDefaultValue(true);

        builder.HasOne(template => template.Team)
            .WithMany()
            .HasForeignKey(template => template.TeamId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(template => template.UpdatedByUser)
            .WithMany()
            .HasForeignKey(template => template.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

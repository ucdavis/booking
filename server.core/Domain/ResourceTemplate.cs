using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class ResourceTemplate
{
    public int Id { get; set; }
    public int? TeamId { get; set; }
    public Team? Team { get; set; }
    public required string Name { get; set; }
    public int FormSchemaVersion { get; set; }
    public required string FormJson { get; set; }

    // Copy-once defaults; the destination team supplies its own billing account.
    public string? ResourceDefaultsJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UpdatedByUserId { get; set; }
    public User UpdatedByUser { get; set; } = null!;

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<ResourceTemplate>();
        builder.ToTable("ResourceTemplates", table =>
        {
            table.HasCheckConstraint("CK_ResourceTemplates_Form", "FormSchemaVersion > 0 AND ISJSON(FormJson) = 1");
            table.HasCheckConstraint("CK_ResourceTemplates_Defaults", "ResourceDefaultsJson IS NULL OR ISJSON(ResourceDefaultsJson) = 1");
        });

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).UseIdentityColumn();
        builder.Property(template => template.Name).HasMaxLength(200).IsRequired();
        builder.Property(template => template.FormJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(template => template.ResourceDefaultsJson).HasColumnType("nvarchar(max)");
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

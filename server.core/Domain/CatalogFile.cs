using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class CatalogFile
{
    public int Id { get; set; }
    public int? SpaceId { get; set; }
    public Space? Space { get; set; }
    public int? ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public required string StorageKey { get; set; }
    public required string Name { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<CatalogFile>();
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

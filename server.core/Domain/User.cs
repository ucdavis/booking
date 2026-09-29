using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class User
{
    public int Id { get; set; }

    public required string IamId { get; set; }

    public required string Name { get; set; }

    public string? Email { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<User>();

        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedOnAdd();
        builder.Property(user => user.IamId).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(user => user.Name).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(320);
        builder.Property(user => user.IsAdmin).HasDefaultValue(false);
        builder.Property(user => user.IsActive).HasDefaultValue(true);

        builder.HasIndex(user => user.IamId).IsUnique();
    }
}

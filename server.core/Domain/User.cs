using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("Users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    [Unicode(false)]
    public required string IamId { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(320)]
    public string? Email { get; set; }

    // TODO: Add a persisted Kerberos login ID only after the column and migration are approved.

    public bool IsAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<User>();

        builder.Property(user => user.IsAdmin).HasDefaultValue(false);
        builder.Property(user => user.IsActive).HasDefaultValue(true);

        builder.HasIndex(user => user.IamId).IsUnique();
    }
}

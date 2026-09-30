using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

[Table("Teams")]
public class Team
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    [Required]
    [MaxLength(100)]
    public required string Slug { get; set; }

    [MaxLength(100)]
    public string? PaymentsTeamSlug { get; set; }

    // Store only the Key Vault secret name; resolve the credential server-side.
    [MaxLength(200)]
    public string? PaymentsApiKeySecretName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Team>();

        builder.HasIndex(team => team.Slug).IsUnique();
    }
}

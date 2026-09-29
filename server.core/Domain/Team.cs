using Microsoft.EntityFrameworkCore;

namespace Server.Core.Domain;

public class Team
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string? PaymentsTeamSlug { get; set; }

    // Store only the Key Vault secret name; resolve the credential server-side.
    public string? PaymentsApiKeySecretName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    protected internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<Team>();

        builder.ToTable("Teams");
        builder.HasKey(team => team.Id);
        builder.Property(team => team.Id).ValueGeneratedOnAdd();
        builder.Property(team => team.Name).HasMaxLength(200).IsRequired();
        builder.Property(team => team.Slug).HasMaxLength(100).IsRequired();
        builder.Property(team => team.PaymentsTeamSlug).HasMaxLength(100);
        builder.Property(team => team.PaymentsApiKeySecretName).HasMaxLength(200);

        builder.HasIndex(team => team.Slug).IsUnique();
    }
}

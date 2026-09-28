namespace Server.Core.Domain.Administration;

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
}

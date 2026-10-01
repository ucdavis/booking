namespace Server.Models.ResourceTemplates;

public sealed class ResourceTemplateResponse
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int FormSchemaVersion { get; init; }
    public required string FormJson { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

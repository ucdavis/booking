namespace Server.Models.Resources;

public sealed class ResourceConfigResponse
{
    public int Id { get; init; }
    public int Version { get; init; }
    public int? SourceTemplateId { get; init; }
    public int FormSchemaVersion { get; init; }
    public string FormJson { get; init; } = string.Empty;
    public int DetailsSchemaVersion { get; init; }
    public string DetailsJson { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
}

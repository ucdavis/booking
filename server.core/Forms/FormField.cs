namespace Server.Core.Forms;

public sealed class FormField
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Label { get; init; }
    public string? HelpText { get; init; }
    public List<FormOption>? Options { get; init; }
    public FormValidation? Validation { get; init; }
}

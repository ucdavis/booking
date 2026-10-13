namespace Server.Models.AggieEnterprise;

public sealed class ChartStringValidationResult
{
    public required string ChartString { get; init; }
    public bool IsValid { get; init; }
    public string? ChartType { get; init; }
    public string[] Messages { get; init; } = [];
    public string Message => string.Join(" ", Messages);
    public string[] Warnings { get; init; } = [];
    public string Warning => string.Join(" ", Warnings);
}

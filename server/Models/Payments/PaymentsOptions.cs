namespace Server.Models.Payments;

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    public string BaseUrl { get; init; } = string.Empty;
}

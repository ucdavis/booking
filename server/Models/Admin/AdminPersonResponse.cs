namespace Server.Models.Admin;

public sealed class AdminPersonResponse
{
    public required string IamId { get; init; }
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? Kerberos { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsActive { get; init; }
}

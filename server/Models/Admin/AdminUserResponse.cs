namespace Server.Models.Admin;

public sealed class AdminUserResponse
{
    public int Id { get; init; }
    public required string IamId { get; init; }
    public required string Name { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; }
}

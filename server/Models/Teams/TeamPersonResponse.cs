using Server.Core.Domain;

namespace Server.Models.Teams;

public sealed class TeamPersonResponse
{
    public required string IamId { get; init; }
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? Kerberos { get; init; }
    public bool IsActive { get; init; }
    public bool? IsActiveInIam { get; init; }
    public TeamRole? Role { get; init; }
}

namespace Server.Models.Emulation;

public sealed class EmulationCandidateResponse
{
    public required string IamId { get; init; }
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? Kerberos { get; init; }
    public bool HasUserAccount { get; init; }
    public bool IsActive { get; init; }
}

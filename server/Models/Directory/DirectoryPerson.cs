namespace Server.Models.Directory;

public sealed class DirectoryPerson
{
    public required string IamId { get; set; }
    public required string Name { get; set; }
    // Campus email only. Health email can match a lookup but is never persisted.
    public string? Email { get; set; }
    public string? Kerberos { get; set; }
    public bool? IsActiveInIam { get; set; }
}

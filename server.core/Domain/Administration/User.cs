namespace Server.Core.Domain.Administration;

public class User
{
    public int Id { get; set; }

    public required string IamId { get; set; }

    public required string Name { get; set; }

    public string? Email { get; set; }

    public bool IsAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}

namespace Server.Core.Domain.Administration;

public class TeamPermission
{
    public int UserId { get; set; }

    public int TeamId { get; set; }

    public required string Role { get; set; }

    public User User { get; set; } = null!;

    public Team Team { get; set; } = null!;
}

namespace Server.Core.Domain.Administration;

public class TeamSpace
{
    public int TeamId { get; set; }

    public int SpaceId { get; set; }

    public Team Team { get; set; } = null!;

    public Space Space { get; set; } = null!;
}

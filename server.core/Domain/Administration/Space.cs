namespace Server.Core.Domain.Administration;

public class Space
{
    public int Id { get; set; }

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string? Address { get; set; }

    public bool IsOfficialFacility { get; set; }

    public string? ReferenceNumber { get; set; }

    public required string TimeZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

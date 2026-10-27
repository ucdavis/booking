using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Server.Models.Spaces;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveSpaceRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }

    [StringLength(500)]
    public string? Address { get; init; }

    public bool IsOfficialFacility { get; init; }

    public bool IsActive { get; init; } = true;

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }
}

using System.ComponentModel.DataAnnotations;

namespace Server.Models.Teams;

public sealed class CreateTeamRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Slug { get; init; } = string.Empty;
}

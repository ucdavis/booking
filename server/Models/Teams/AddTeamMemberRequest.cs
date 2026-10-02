using System.ComponentModel.DataAnnotations;
using Server.Core.Domain;

namespace Server.Models.Teams;

public sealed class AddTeamMemberRequest
{
    [Required]
    [StringLength(10)]
    public string IamId { get; init; } = string.Empty;

    [Required]
    public TeamRole? Role { get; init; }
}

using System.ComponentModel.DataAnnotations;
using Server.Core.Domain;

namespace Server.Models.Teams;

public sealed class UpdateTeamMemberRoleRequest
{
    [Required]
    public TeamRole? Role { get; init; }
}

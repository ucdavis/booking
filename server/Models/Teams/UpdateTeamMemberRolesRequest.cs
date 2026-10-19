using System.ComponentModel.DataAnnotations;
using Server.Core.Domain;

namespace Server.Models.Teams;

public sealed class UpdateTeamMemberRolesRequest
{
    [Required]
    public List<TeamRole>? Roles { get; init; }

    public bool ConfirmSelfAccessChange { get; init; }
}

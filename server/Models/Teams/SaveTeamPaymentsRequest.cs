using System.ComponentModel.DataAnnotations;

namespace Server.Models.Teams;

public sealed class SaveTeamPaymentsRequest
{
    [Required]
    [MaxLength(4096)]
    public string ApiKey { get; init; } = string.Empty;
}

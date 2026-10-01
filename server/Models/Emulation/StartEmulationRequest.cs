using System.ComponentModel.DataAnnotations;

namespace Server.Models.Emulation;

public sealed class StartEmulationRequest
{
    [Required]
    [StringLength(50)]
    public string IamId { get; init; } = string.Empty;
}

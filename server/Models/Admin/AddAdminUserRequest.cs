using System.ComponentModel.DataAnnotations;

namespace Server.Models.Admin;

public sealed class AddAdminUserRequest
{
    [Required]
    [StringLength(10)]
    public string IamId { get; init; } = string.Empty;
}

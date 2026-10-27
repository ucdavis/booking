using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Server.Core.Forms;

namespace Server.Models.Resources;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveResourceRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Slug { get; init; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Category { get; init; } = string.Empty;

    [StringLength(10000)]
    public string? Description { get; init; }

    [Range(1, int.MaxValue)]
    public int? Capacity { get; init; }

    [JsonRequired]
    public bool IsActive { get; init; } = true;

    [JsonRequired]
    public bool IsReservable { get; init; } = true;

    [Required]
    public string ApprovalMode { get; init; } = "manual";

    [JsonRequired]
    public bool FollowTeamCalendar { get; init; } = true;

    public string? PublicAccess { get; init; } = "availability";

    [JsonRequired]
    public bool AllowReservationSharing { get; init; }

    [Range(1, int.MaxValue)]
    public int? SpaceId { get; init; }

    [Range(1, int.MaxValue)]
    public int? SourceTemplateId { get; init; }

    [Range(1, int.MaxValue)]
    public int? FormSchemaVersion { get; init; }

    [StringLength(FormDefinitionValidator.MaxJsonLength)]
    public string? FormJson { get; init; }

    [Range(1, int.MaxValue)]
    public int? DetailsSchemaVersion { get; init; }

    [StringLength(FormDefinitionValidator.MaxJsonLength)]
    public string? DetailsJson { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    [Range(1, int.MaxValue)]
    public int? ExpectedConfigId { get; init; }
}

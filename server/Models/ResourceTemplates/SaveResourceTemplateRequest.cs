using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Server.Core.Forms;

namespace Server.Models.ResourceTemplates;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SaveResourceTemplateRequest
{
    public const int MaxResourceDefaultsJsonLength = 1024 * 1024;
    private string? _resourceDefaultsJson;

    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Range(1, int.MaxValue)]
    public int FormSchemaVersion { get; init; }

    [Required]
    [StringLength(FormDefinitionValidator.MaxJsonLength)]
    public string FormJson { get; init; } = string.Empty;

    [StringLength(MaxResourceDefaultsJsonLength)]
    public string? ResourceDefaultsJson
    {
        get => _resourceDefaultsJson;
        init
        {
            _resourceDefaultsJson = value;
            HasResourceDefaultsJson = true;
        }
    }

    // Older clients omit this field; only an explicit value should replace saved defaults.
    [JsonIgnore]
    public bool HasResourceDefaultsJson { get; private set; }

    [JsonRequired]
    public bool IsActive { get; init; } = true;

    public DateTimeOffset? UpdatedAt { get; init; }
}

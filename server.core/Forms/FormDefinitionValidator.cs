using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Server.Core.Forms;

public static class FormDefinitionValidator
{
    public const int MaxJsonLength = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16,
    };

    private static readonly Regex IdentifierPattern = new(@"\A[A-Za-z0-9_-]{1,100}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryParse(int schemaVersion, string? formJson, out FormDefinition? definition, out string? error)
    {
        definition = null;
        // FormSchemaVersion identifies a saved revision. The document shape determines
        // whether this builder can understand it, independently of its revision number.
        if (schemaVersion < 1)
        {
            error = "The form version must be a positive integer.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(formJson) || formJson.Length > MaxJsonLength)
        {
            error = "The form definition is required and must be no larger than 1 MiB of text.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(formJson, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || HasNullOrDuplicateProperties(document.RootElement))
            {
                error = "The form definition must be an object without null values or duplicate properties.";
                return false;
            }

            var candidate = document.RootElement.Deserialize<FormDefinition>(SerializerOptions);
            if (candidate?.Fields == null || candidate.Fields.Count > 100)
            {
                error = "A form must contain a fields array with no more than 100 fields.";
                return false;
            }

            var fieldIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in candidate.Fields)
            {
                error = ValidateField(field, fieldIds);
                if (error != null)
                {
                    return false;
                }
            }

            definition = candidate;
            error = null;
            return true;
        }
        catch (JsonException)
        {
            error = "The form definition contains invalid JSON, missing properties, or unsupported properties or values.";
            return false;
        }
    }

    public static bool AreEquivalent(FormDefinition first, FormDefinition second)
        => Canonicalize(first) == Canonicalize(second);

    // Preserve field and option order while treating omitted optional defaults identically.
    private static string Canonicalize(FormDefinition definition)
        => JsonSerializer.Serialize(definition.Fields.Select(field => new
        {
            field.Id,
            field.Type,
            field.Label,
            HelpText = field.HelpText ?? string.Empty,
            Options = field.Options?.Select(option => new { option.Id, option.Label }),
            Required = field.Validation?.Required ?? false,
            MinLength = field.Validation?.MinLength,
            MaxLength = field.Validation?.MaxLength,
        }), SerializerOptions);

    private static string? ValidateField(FormField? field, HashSet<string> fieldIds)
    {
        if (field == null || !IsValidId(field.Id) || !fieldIds.Add(field.Id))
        {
            return "Each field must have a unique ID of 1 to 100 letters, numbers, underscores, or hyphens.";
        }
        if (string.IsNullOrWhiteSpace(field.Label) || field.Label.Length > 10000)
        {
            return "Each field needs a label or text of no more than 10,000 characters.";
        }
        if (field.HelpText?.Length > 2000)
        {
            return "Field help text must be no more than 2,000 characters.";
        }

        var isTextInput = field.Type == "input" || field.Type == "textarea";
        var isChoice = field.Type == "checkboxes" || field.Type == "dropdown" || field.Type == "radio";
        if (!isTextInput && !isChoice && field.Type != "text")
        {
            return "Choose a supported field type: input, textarea, checkboxes, dropdown, radio, or text.";
        }
        if (field.Type == "text" && field.Validation != null)
        {
            return "Text blocks cannot have validation rules.";
        }
        if (!isChoice && field.Options != null)
        {
            return "Only checkboxes, dropdowns, and radio buttons can have options.";
        }
        if (isChoice)
        {
            if (field.Options == null || field.Options.Count == 0 || field.Options.Count > 100)
            {
                return "Checkboxes, dropdowns, and radio buttons need between 1 and 100 options.";
            }

            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in field.Options)
            {
                if (option == null || !IsValidId(option.Id) || !optionIds.Add(option.Id))
                {
                    return "Each option must have a unique ID within its field of 1 to 100 letters, numbers, underscores, or hyphens.";
                }
                if (string.IsNullOrWhiteSpace(option.Label) || option.Label.Length > 200)
                {
                    return "Each option needs a label of no more than 200 characters.";
                }
            }
        }

        var validation = field.Validation;
        if (validation == null)
        {
            return null;
        }
        if (!isTextInput && (validation.MinLength != null || validation.MaxLength != null))
        {
            return "Length rules are only available for inputs and text areas.";
        }
        if (validation.MinLength < 1 || validation.MinLength > 100000 ||
            validation.MaxLength < 1 || validation.MaxLength > 100000)
        {
            return "Minimum and maximum lengths must be whole numbers between 1 and 100,000.";
        }
        if (validation.MinLength > validation.MaxLength)
        {
            return "Minimum length cannot exceed maximum length.";
        }

        return null;
    }

    private static bool IsValidId(string? id) => id != null && IdentifierPattern.IsMatch(id);

    private static bool HasNullOrDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasNullOrDuplicateProperties(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasNullOrDuplicateProperties(item))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

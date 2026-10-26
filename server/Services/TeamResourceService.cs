using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Forms;
using Server.Models.Resources;
using Server.Models.ResourceTemplates;

namespace Server.Services;

public sealed class TeamResourceService(AppDbContext dbContext)
{
    private const string StaleMessage = "This resource has changed since you opened it. Reload it before saving again.";
    private static readonly string[] Days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    public Task<List<ResourceSummaryResponse>> GetResources(int teamId, CancellationToken cancellationToken)
        => dbContext.Resources.AsNoTracking().Where(resource => resource.TeamId == teamId)
            .OrderBy(resource => resource.Space.Name).ThenBy(resource => resource.Name).ThenBy(resource => resource.Id)
            .Select(resource => new ResourceSummaryResponse
            {
                Id = resource.Id, TeamId = resource.TeamId, SpaceId = resource.SpaceId, SpaceName = resource.Space.Name,
                Name = resource.Name, Slug = resource.Slug, Category = resource.Category, Capacity = resource.Capacity,
                IsActive = resource.IsActive, IsReservable = resource.IsReservable, UpdatedAt = resource.UpdatedAt,
                ParentResourceId = resource.ParentResourceId,
            }).ToListAsync(cancellationToken);

    public async Task<ResourceOptionsResponse> GetOptions(int teamId, CancellationToken cancellationToken)
    {
        var spaces = await dbContext.TeamSpaces.AsNoTracking()
            .Where(link => link.TeamId == teamId && link.Space.IsActive)
            .OrderBy(link => link.Space.Name).ThenBy(link => link.SpaceId)
            .Select(link => new ResourceSpaceResponse
            {
                Id = link.SpaceId, Name = link.Space.Name, Address = link.Space.Address, TimeZoneId = link.Space.TimeZoneId,
            }).ToListAsync(cancellationToken);
        var templates = await dbContext.ResourceTemplates.AsNoTracking()
            .Where(template => template.IsActive && (template.TeamId == null || template.TeamId == teamId))
            .OrderBy(template => template.Name).ThenBy(template => template.Id)
            .Select(template => new ResourceTemplateResponse
            {
                Id = template.Id, TeamId = template.TeamId, Name = template.Name, Description = template.Description,
                FormSchemaVersion = template.FormSchemaVersion, FormJson = template.FormJson,
                ResourceDefaultsJson = template.ResourceDefaultsJson, IsActive = template.IsActive,
                CreatedAt = template.CreatedAt, UpdatedAt = template.UpdatedAt,
            }).ToListAsync(cancellationToken);
        return new ResourceOptionsResponse { Spaces = spaces, Templates = templates };
    }

    public async Task<ResourceDetailResponse?> GetResource(int teamId, int id, CancellationToken cancellationToken)
    {
        var resource = await dbContext.Resources.AsNoTracking().Include(resource => resource.Space)
            .SingleOrDefaultAsync(resource => resource.TeamId == teamId && resource.Id == id, cancellationToken);
        return resource == null ? null : ToResponse(resource, await LatestConfig(id, cancellationToken));
    }

    public async Task<(ResourceDetailResponse? Resource, string? Error, int StatusCode)> SaveResource(
        int teamId, int userId, int? id, SaveResourceRequest request, CancellationToken cancellationToken)
    {
        var error = ValidateRequest(request, out var nameKey);
        if (error != null)
        {
            return (null, error, 400);
        }

        // Keep resource and configuration changes together. Serializable reads also
        // protect location availability, sibling names and parent activation checks against concurrent saves.
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var result = id == null
                ? await CreateResource(teamId, userId, request, nameKey, cancellationToken)
                : await UpdateResource(teamId, userId, id.Value, request, nameKey, cancellationToken);
            if (result.Resource != null && transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        catch (DbUpdateException exception) when (IsConcurrentConflict(exception.InnerException))
        {
            return (null, "Another resource or location was saved at the same time. Reload and check the name before trying again.", 409);
        }
        catch (SqlException exception) when (IsConcurrentConflict(exception))
        {
            return (null, "Another resource or location was saved at the same time. Reload before trying again.", 409);
        }
    }

    private async Task<(ResourceDetailResponse? Resource, string? Error, int StatusCode)> CreateResource(
        int teamId, int userId, SaveResourceRequest request, string nameKey, CancellationToken cancellationToken)
    {
        if (request.UpdatedAt != null || request.ExpectedConfigId != null)
        {
            return (null, "A new resource cannot reference an existing saved resource or configuration.", 400);
        }
        if (request.SpaceId == null)
        {
            return (null, "Choose an active location associated with this team.", 400);
        }
        var template = await dbContext.ResourceTemplates.AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == request.SourceTemplateId && template.IsActive &&
                (template.TeamId == null || template.TeamId == teamId), cancellationToken);
        if (template == null)
        {
            return (null, "Choose an active resource template available to this team.", 400);
        }

        var formJson = request.FormJson ?? template.FormJson;
        var formSchemaVersion = request.FormSchemaVersion ?? template.FormSchemaVersion;
        var error = request.FormJson == null
            ? ValidateCopiedDocument(formJson, formSchemaVersion)
            : ValidateForm(formSchemaVersion, formJson);
        if (error != null)
        {
            return (null, error, 400);
        }
        var detailsJson = request.DetailsJson ?? template.ResourceDefaultsJson ?? "{}";
        error = ValidateDetails(detailsJson, request.DetailsSchemaVersion, request.DetailsJson != null, out var detailsSchemaVersion);
        if (error != null)
        {
            return (null, error, 400);
        }
        if (!ValidPublicAccess(request.PublicAccess))
        {
            return (null, "Choose a public visibility setting for this resource.", 400);
        }

        var space = await dbContext.TeamSpaces.AsNoTracking()
            .Where(link => link.TeamId == teamId && link.SpaceId == request.SpaceId && link.Space.IsActive)
            .Select(link => link.Space).SingleOrDefaultAsync(cancellationToken);
        if (space == null)
        {
            return (null, "Choose an active location associated with this team.", 400);
        }
        if (await DuplicateName(space.Id, null, nameKey, null, cancellationToken))
        {
            return (null, "A resource with that name already exists at this location, including inactive resources.", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var resource = new Resource
        {
            TeamId = teamId, SpaceId = space.Id, Name = request.Name.Trim(), NameKey = nameKey,
            Category = request.Category.Trim(), Slug = CreateSlug(request.Name), CreatedAt = now,
        };
        ApplyMetadata(resource, request, nameKey, now, userId);
        dbContext.Resources.Add(resource);
        var config = NewConfig(resource.Id, userId, 1, template.Id, formSchemaVersion, formJson,
            detailsSchemaVersion, detailsJson, now);
        config.Resource = resource;
        dbContext.ResourceConfigs.Add(config);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (ToResponse(resource, config, space.Name), null, 201);
    }

    private async Task<(ResourceDetailResponse? Resource, string? Error, int StatusCode)> UpdateResource(
        int teamId, int userId, int id, SaveResourceRequest request, string nameKey, CancellationToken cancellationToken)
    {
        var resource = await dbContext.Resources.AsNoTracking().Include(resource => resource.Space)
            .SingleOrDefaultAsync(resource => resource.Id == id && resource.TeamId == teamId, cancellationToken);
        if (resource == null)
        {
            return (null, "That resource could not be found.", 404);
        }
        var config = await LatestConfig(id, cancellationToken);
        if (request.UpdatedAt == null)
        {
            return (null, "The saved resource timestamp is required when updating a resource.", 400);
        }
        if (request.UpdatedAt.Value != resource.UpdatedAt || request.ExpectedConfigId != config?.Id)
        {
            return (null, StaleMessage, 409);
        }
        if (request.SpaceId != null && request.SpaceId != resource.SpaceId)
        {
            return (null, "A resource's location cannot be changed here.", 400);
        }
        if (request.SourceTemplateId != null && request.SourceTemplateId != config?.SourceTemplateId)
        {
            return (null, "An existing resource keeps its original source template. Edit its own configuration instead.", 400);
        }
        if (resource.ParentResourceId == null && !ValidPublicAccess(request.PublicAccess))
        {
            return (null, "Choose a public visibility setting for this resource.", 400);
        }
        if (await DuplicateName(resource.SpaceId, resource.ParentResourceId, nameKey, id, cancellationToken))
        {
            return (null, "A sibling resource with that name already exists at this location, including inactive resources.", 409);
        }
        var error = await ValidateActivation(resource, request.IsActive, cancellationToken);
        if (error != null)
        {
            return (null, error, 409);
        }

        var formChanged = false;
        var detailsChanged = false;
        if (request.FormJson != null || request.DetailsJson != null)
        {
            if (config == null)
            {
                return (null, "This resource has no saved configuration to edit.", 400);
            }
            if (request.FormJson != null)
            {
                error = ValidateForm(request.FormSchemaVersion!.Value, request.FormJson);
                if (error != null)
                {
                    return (null, error, 400);
                }
                formChanged = request.FormSchemaVersion != config.FormSchemaVersion ||
                    !FormsEquivalent(config, request.FormJson);
            }
            if (request.DetailsJson != null)
            {
                error = ValidateDetails(request.DetailsJson, request.DetailsSchemaVersion, true, out _);
                if (error != null)
                {
                    return (null, error, 400);
                }
                detailsChanged = request.DetailsSchemaVersion != config.DetailsSchemaVersion ||
                    !string.Equals(request.DetailsJson, config.DetailsJson, StringComparison.Ordinal);
            }
        }
        if ((formChanged || detailsChanged) && config!.Version == int.MaxValue)
        {
            return (null, "This resource has reached the maximum configuration version.", 409);
        }
        if (resource.UpdatedAt == DateTimeOffset.MaxValue)
        {
            return (null, "This resource timestamp cannot advance. The resource has not been changed.", 409);
        }
        var now = DateTimeOffset.UtcNow;
        if (now <= resource.UpdatedAt)
        {
            now = resource.UpdatedAt.AddTicks(1);
        }
        var publicAccess = resource.ParentResourceId == null ? request.PublicAccess : null;
        var original = dbContext.Resources.Where(current => current.Id == id && current.TeamId == teamId &&
            current.UpdatedAt == request.UpdatedAt.Value);
        if (dbContext.Database.IsRelational())
        {
            var updated = await original.ExecuteUpdateAsync(setters => setters
                .SetProperty(current => current.Name, request.Name.Trim())
                .SetProperty(current => current.NameKey, nameKey)
                .SetProperty(current => current.Category, request.Category.Trim())
                .SetProperty(current => current.Description, TrimOptional(request.Description))
                .SetProperty(current => current.Capacity, request.Capacity)
                .SetProperty(current => current.IsActive, request.IsActive)
                .SetProperty(current => current.IsReservable, request.IsReservable)
                .SetProperty(current => current.ApprovalMode, request.ApprovalMode)
                .SetProperty(current => current.FollowTeamCalendar, request.FollowTeamCalendar)
                .SetProperty(current => current.PublicAccess, publicAccess)
                .SetProperty(current => current.AllowReservationSharing, request.AllowReservationSharing)
                .SetProperty(current => current.UpdatedAt, now)
                .SetProperty(current => current.UpdatedByUserId, userId), cancellationToken);
            if (updated == 0)
            {
                return (null, StaleMessage, 409);
            }
        }
        else
        {
            var tracked = await original.SingleOrDefaultAsync(cancellationToken);
            if (tracked == null)
            {
                return (null, StaleMessage, 409);
            }
            ApplyMetadata(tracked, request, nameKey, now, userId);
        }
        if (formChanged || detailsChanged)
        {
            // Reservations continue to reference the old immutable row. The new row
            // becomes the configuration used for subsequent requests.
            var next = NewConfig(resource.Id, userId, config!.Version + 1, config.SourceTemplateId,
                formChanged ? request.FormSchemaVersion!.Value : config.FormSchemaVersion,
                formChanged ? request.FormJson! : config.FormJson,
                detailsChanged ? request.DetailsSchemaVersion!.Value : config.DetailsSchemaVersion,
                detailsChanged ? request.DetailsJson! : config.DetailsJson, now);
            dbContext.ResourceConfigs.Add(next);
            config = next;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        ApplyMetadata(resource, request, nameKey, now, userId);
        return (ToResponse(resource, config), null, 200);
    }

    private Task<ResourceConfig?> LatestConfig(int id, CancellationToken cancellationToken)
        => dbContext.ResourceConfigs.AsNoTracking().Where(config => config.ResourceId == id)
            .OrderByDescending(config => config.Version).FirstOrDefaultAsync(cancellationToken);

    private Task<bool> DuplicateName(int spaceId, int? parentId, string nameKey, int? exceptId, CancellationToken cancellationToken)
        => dbContext.Resources.AnyAsync(resource => resource.SpaceId == spaceId && resource.ParentResourceId == parentId &&
            resource.NameKey == nameKey && (exceptId == null || resource.Id != exceptId), cancellationToken);

    private async Task<string?> ValidateActivation(Resource resource, bool isActive, CancellationToken cancellationToken)
    {
        if (!isActive)
        {
            return await dbContext.Resources.AnyAsync(child => child.ParentResourceId == resource.Id && child.IsActive, cancellationToken)
                ? "Deactivate this resource's active children before deactivating the parent." : null;
        }
        if (!resource.Space.IsActive)
        {
            return "This resource cannot be active while its location is inactive.";
        }
        var parentId = resource.ParentResourceId;
        var visited = new HashSet<int> { resource.Id };
        while (parentId != null)
        {
            if (!visited.Add(parentId.Value))
            {
                return "This resource's parent hierarchy must be corrected before it can be activated.";
            }
            var parent = await dbContext.Resources.AsNoTracking().Where(current => current.Id == parentId &&
                    current.SpaceId == resource.SpaceId && current.TeamId == resource.TeamId)
                .Select(current => new { current.IsActive, current.ParentResourceId }).SingleOrDefaultAsync(cancellationToken);
            if (parent == null || !parent.IsActive)
            {
                return "Activate this resource's parent resources before activating this resource.";
            }
            parentId = parent.ParentResourceId;
        }
        return null;
    }

    private static string? ValidateRequest(SaveResourceRequest? request, out string nameKey)
    {
        nameKey = string.Empty;
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return "Enter a resource name of no more than 200 characters.";
        }
        try
        {
            nameKey = Regex.Replace(request.Name.Trim(), @"\s+", " ").Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return "Enter a resource name using valid text.";
        }
        if (nameKey.Length > 200 || nameKey.Any(char.IsControl))
        {
            return "Enter a resource name of no more than 200 characters without control characters.";
        }
        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 50)
        {
            return "Enter a resource category of no more than 50 characters.";
        }
        if (request.Description?.Length > 10000 || request.Capacity <= 0)
        {
            return "Enter a positive capacity and a description of no more than 10,000 characters.";
        }
        if (request.ApprovalMode != "automatic" && request.ApprovalMode != "manual")
        {
            return "Choose automatic or manual reservation approval.";
        }
        if ((request.FormJson == null) != (request.FormSchemaVersion == null) ||
            (request.DetailsJson == null) != (request.DetailsSchemaVersion == null))
        {
            return "Provide each configuration document together with its schema version, or omit both to preserve it.";
        }
        return null;
    }

    private static string? ValidateForm(int version, string json)
    {
        if (!FormDefinitionValidator.TryParse(version, json, out var definition, out var error))
        {
            return error;
        }
        return definition!.Fields.Count == 0 ? "Add at least one reservation form field." : null;
    }

    private static bool FormsEquivalent(ResourceConfig saved, string requested)
        => FormDefinitionValidator.TryParse(saved.FormSchemaVersion, saved.FormJson, out var savedForm, out _) &&
            FormDefinitionValidator.TryParse(saved.FormSchemaVersion, requested, out var requestedForm, out _) &&
            FormDefinitionValidator.AreEquivalent(savedForm!, requestedForm!);

    private static string? ValidateCopiedDocument(string json, int version)
    {
        if (version < 1 || string.IsNullOrWhiteSpace(json) || json.Length > FormDefinitionValidator.MaxJsonLength)
        {
            return "The template configuration needs a positive version and valid JSON of no more than 1 MiB of text.";
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object || document.RootElement.ValueKind == JsonValueKind.Array
                ? null : "The template configuration must be a JSON object or array.";
        }
        catch (JsonException)
        {
            return "The template configuration must contain valid JSON.";
        }
    }

    private static string? ValidateDetails(string json, int? requestedVersion, bool editing, out int version)
    {
        version = requestedVersion ?? 1;
        if (string.IsNullOrWhiteSpace(json) || json.Length > FormDefinitionValidator.MaxJsonLength)
        {
            return "Resource settings must contain valid JSON of no more than 1 MiB of text.";
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object && root.ValueKind != JsonValueKind.Array)
            {
                return "Resource settings must be a JSON object or array.";
            }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schemaVersion", out var embeddedVersion))
            {
                if (embeddedVersion.ValueKind != JsonValueKind.Number || !embeddedVersion.TryGetInt32(out var embedded) || embedded < 1)
                {
                    return editing ? "Resource settings need a positive schema version." : null;
                }
                version = requestedVersion ?? embedded;
                if (version != embedded)
                {
                    return "The resource settings schema versions must match.";
                }
            }
            if (editing && (version != 1 || root.ValueKind != JsonValueKind.Object))
            {
                return "This settings format cannot be edited here. Preserve the saved settings to edit the resource's other fields.";
            }
            // Unknown template formats are copied intact, so future versions can be
            // consumed without this editor discarding fields it does not understand.
            if (!editing || version != 1 || root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (root.TryGetProperty("openingHours", out var hours))
            {
                if (hours.ValueKind != JsonValueKind.Object)
                {
                    return "Opening hours must contain days with opening and closing periods.";
                }
                foreach (var day in Days)
                {
                    if (!hours.TryGetProperty(day, out var periods))
                    {
                        continue;
                    }
                    if (periods.ValueKind != JsonValueKind.Array)
                    {
                        return $"Enter valid opening periods for {day}.";
                    }
                    var intervals = new List<(TimeOnly Start, TimeOnly End)>();
                    foreach (var period in periods.EnumerateArray())
                    {
                        if (period.ValueKind != JsonValueKind.Object ||
                            !TryText(period, "start", out var startText) || !TryText(period, "end", out var endText) ||
                            !TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                            !TimeOnly.TryParseExact(endText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || start >= end)
                        {
                            return $"For {day}, each closing time must be after its opening time on the same day.";
                        }
                        intervals.Add((start, end));
                    }
                    var sorted = intervals.OrderBy(interval => interval.Start).ToArray();
                    for (var index = 1; index < sorted.Length; index++)
                    {
                        if (sorted[index].Start < sorted[index - 1].End)
                        {
                            return $"Opening periods for {day} must not overlap.";
                        }
                    }
                }
            }
            if (root.TryGetProperty("billingRates", out var rates))
            {
                if (rates.ValueKind != JsonValueKind.Array)
                {
                    return "Billing rates must be a list of rates.";
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var rate in rates.EnumerateArray())
                {
                    if (rate.ValueKind != JsonValueKind.Object || !TryText(rate, "id", out var id) ||
                        string.IsNullOrWhiteSpace(id) || !ids.Add(id) || !TryText(rate, "name", out var name) ||
                        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || !TryText(rate, "amount", out var amount) ||
                        !Regex.IsMatch(amount, @"\A[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) ||
                        !TryText(rate, "basis", out var basis) || (basis != "hour" && basis != "day" && basis != "booking"))
                    {
                        return "Each billing rate needs a unique ID, a name of no more than 100 characters, a nonnegative amount, and a valid billing basis.";
                    }
                    if (rate.TryGetProperty("currency", out var currency) &&
                        (currency.ValueKind != JsonValueKind.String || currency.GetString() != "USD"))
                    {
                        return "Only US dollar billing rates are supported.";
                    }
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return "Resource settings must contain valid JSON.";
        }
    }

    private static bool TryText(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var child) || child.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = child.GetString()!;
        return true;
    }

    private static bool ValidPublicAccess(string? value) => value == "none" || value == "availability" || value == "details";

    private static string? TrimOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateSlug(string name)
    {
        var normalized = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            var lower = char.ToLowerInvariant(character);
            builder.Append(lower >= 'a' && lower <= 'z' || lower >= '0' && lower <= '9' ? lower : '-');
        }
        var prefix = Regex.Replace(builder.ToString(), "-+", "-").Trim('-');
        if (prefix.Length == 0)
        {
            prefix = "resource";
        }
        prefix = prefix[..Math.Min(prefix.Length, 80)].TrimEnd('-');
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static bool IsConcurrentConflict(Exception? exception)
        => exception is SqlException sql && (sql.Number == 2601 || sql.Number == 2627 || sql.Number == 1205);

    private static void ApplyMetadata(Resource resource, SaveResourceRequest request, string nameKey, DateTimeOffset now, int userId)
    {
        resource.Name = request.Name.Trim();
        resource.NameKey = nameKey;
        resource.Category = request.Category.Trim();
        resource.Description = TrimOptional(request.Description);
        resource.Capacity = request.Capacity;
        resource.IsActive = request.IsActive;
        resource.IsReservable = request.IsReservable;
        resource.ApprovalMode = request.ApprovalMode;
        resource.FollowTeamCalendar = request.FollowTeamCalendar;
        resource.PublicAccess = resource.ParentResourceId == null ? request.PublicAccess : null;
        resource.AllowReservationSharing = request.AllowReservationSharing;
        resource.UpdatedAt = now;
        resource.UpdatedByUserId = userId;
    }

    private static ResourceConfig NewConfig(int resourceId, int userId, int version, int? templateId,
        int formVersion, string formJson, int detailsVersion, string detailsJson, DateTimeOffset now) => new()
    {
        ResourceId = resourceId, Version = version, SourceTemplateId = templateId,
        FormSchemaVersion = formVersion, FormJson = formJson, DetailsSchemaVersion = detailsVersion, DetailsJson = detailsJson,
        CreatedByUserId = userId, CreatedAt = now, UpdatedAt = now, PublishedByUserId = userId, PublishedAt = now,
    };

    private static ResourceDetailResponse ToResponse(Resource resource, ResourceConfig? config, string? spaceName = null) => new()
    {
        Id = resource.Id, TeamId = resource.TeamId, SpaceId = resource.SpaceId, SpaceName = spaceName ?? resource.Space.Name,
        Name = resource.Name, Slug = resource.Slug, Category = resource.Category, Capacity = resource.Capacity,
        IsActive = resource.IsActive, IsReservable = resource.IsReservable, UpdatedAt = resource.UpdatedAt,
        ParentResourceId = resource.ParentResourceId, Description = resource.Description, ApprovalMode = resource.ApprovalMode,
        FollowTeamCalendar = resource.FollowTeamCalendar, PublicAccess = resource.PublicAccess,
        AllowReservationSharing = resource.AllowReservationSharing, CreatedAt = resource.CreatedAt,
        Config = config == null ? null : new ResourceConfigResponse
        {
            Id = config.Id, Version = config.Version, SourceTemplateId = config.SourceTemplateId,
            FormSchemaVersion = config.FormSchemaVersion, FormJson = config.FormJson,
            DetailsSchemaVersion = config.DetailsSchemaVersion, DetailsJson = config.DetailsJson, PublishedAt = config.PublishedAt,
        },
    };
}

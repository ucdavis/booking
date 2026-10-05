using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.ResourceTemplates;
using Server.Services;

namespace Server.Tests.Controllers;

public class AdminResourceTemplatesControllerTests
{
    private const string FormJson = """
        {"fields":[{"id":"name","type":"input","label":"Your name","validation":{"required":true,"maxLength":100}}]}
        """;
    private const string ChangedFormJson = """
        {"fields":[{"id":"purpose","type":"input","label":"Purpose"}]}
        """;
    private const string EmptyFormJson = "{\"fields\":[]}";
    private const string TextOnlyFormJson = """
        {"fields":[{"id":"intro","type":"text","label":"Please read these instructions."}]}
        """;

    [Fact]
    public async Task Reads_only_global_templates_including_inactive_templates_without_tracking()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.ResourceTemplates.AddRange(
            CreateTemplate("Zoology"),
            CreateTemplate("Archived", isActive: false),
            CreateTemplate("Team only", teamId: 7));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = CreateController(db);

        var templates = ReadOk(await controller.GetTemplates());

        templates.Select(template => template.Name).Should().Equal("Archived", "Zoology");
        templates[0].IsActive.Should().BeFalse();
        ReadOk(await controller.GetTemplate(templates[0].Id)).IsActive.Should().BeFalse();
        ReadOk(await controller.GetTemplate(templates[1].Id)).FormJson.Should().Be(FormJson);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_saves_global_template_with_effective_admin_audit_and_trimmed_name()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var admin = await AddAdmin(db);
        var started = DateTimeOffset.UtcNow;

        var result = await CreateController(db).CreateTemplate(CreateRequest("  Room request  ", isActive: false));

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(AdminResourceTemplatesController.GetTemplate));
        var response = created.Value.Should().BeOfType<ResourceTemplateResponse>().Subject;
        created.RouteValues!["id"].Should().Be(response.Id);
        db.ChangeTracker.Clear();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Id.Should().Be(response.Id);
        saved.TeamId.Should().BeNull();
        saved.Name.Should().Be("Room request");
        saved.FormSchemaVersion.Should().Be(1);
        saved.FormJson.Should().Be(FormJson);
        saved.ResourceDefaultsJson.Should().BeNull();
        saved.IsActive.Should().BeFalse();
        saved.UpdatedByUserId.Should().Be(admin.Id);
        saved.CreatedAt.Should().BeOnOrAfter(started);
        saved.UpdatedAt.Should().Be(saved.CreatedAt);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n", null)]
    [InlineData(" {} ", " {} ")]
    [InlineData("[ {\"futureSetting\":true} ]", "[ {\"futureSetting\":true} ]")]
    [InlineData("{\"openHours\":{\"monday\":[\"09:00\",\"17:00\"]},\"billingRates\":[{\"amount\":25}]}",
        "{\"openHours\":{\"monday\":[\"09:00\",\"17:00\"]},\"billingRates\":[{\"amount\":25}]}")]
    public async Task Create_and_reads_preserve_optional_defaults_without_imposing_a_schema(string? defaultsJson, string? expected)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var controller = CreateController(db);

        var result = await controller.CreateTemplate(new SaveResourceTemplateRequest
        {
            Name = "Room", FormSchemaVersion = 1, FormJson = FormJson, IsActive = true,
            ResourceDefaultsJson = defaultsJson,
        });

        var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<ResourceTemplateResponse>().Subject;
        response.ResourceDefaultsJson.Should().Be(expected);
        db.ChangeTracker.Clear();
        (await db.ResourceTemplates.SingleAsync()).ResourceDefaultsJson.Should().Be(expected);
        ReadOk(await controller.GetTemplate(response.Id)).ResourceDefaultsJson.Should().Be(expected);
        ReadOk(await controller.GetTemplates()).Single().ResourceDefaultsJson.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n", null)]
    [InlineData(" {} ", " {} ")]
    [InlineData("[ {\"futureSetting\":true} ]", "[ {\"futureSetting\":true} ]")]
    public async Task Defaults_can_be_replaced_or_cleared_without_changing_the_id_or_form_version(string? defaultsJson, string? expected)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        template.FormSchemaVersion = 3;
        template.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var originalUpdated = template.UpdatedAt;
        var originalCreated = template.CreatedAt;
        db.ChangeTracker.Clear();

        var response = ReadOk(await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = template.Name, FormSchemaVersion = 3, FormJson = FormJson, IsActive = true,
            ResourceDefaultsJson = defaultsJson, UpdatedAt = originalUpdated,
        }));

        response.Id.Should().Be(template.Id);
        response.FormSchemaVersion.Should().Be(3);
        response.FormJson.Should().Be(FormJson);
        response.ResourceDefaultsJson.Should().Be(expected);
        response.UpdatedAt.Should().BeAfter(originalUpdated);
        db.ChangeTracker.Clear();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.ResourceDefaultsJson.Should().Be(expected);
        saved.CreatedAt.Should().Be(originalCreated);
        saved.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"billingRates\":[{\"amount\":25}],\"unknownSetting\":true}")]
    [InlineData("[ {\"openHours\":[]} ]")]
    public async Task Form_and_defaults_changes_save_requested_defaults_only_on_the_new_revision(string? defaultsJson)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        template.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = template.Name, FormSchemaVersion = 1, FormJson = ChangedFormJson, IsActive = true,
            ResourceDefaultsJson = defaultsJson, UpdatedAt = template.UpdatedAt,
        });

        var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<ResourceTemplateResponse>().Subject;
        response.Id.Should().NotBe(template.Id);
        response.FormSchemaVersion.Should().Be(2);
        response.ResourceDefaultsJson.Should().Be(defaultsJson);
        db.ChangeTracker.Clear();
        var successor = await db.ResourceTemplates.SingleAsync(current => current.Id == response.Id);
        successor.ResourceDefaultsJson.Should().Be(defaultsJson);
        successor.FormJson.Should().Be(ChangedFormJson);
        var archived = await db.ResourceTemplates.SingleAsync(current => current.Id == template.Id);
        archived.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        archived.FormJson.Should().Be(FormJson);
        archived.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("{invalid}")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("12")]
    [InlineData("\"text\"")]
    [InlineData("{} {}")]
    [InlineData("{\"value\":1,}")]
    public async Task Invalid_defaults_are_rejected_on_create_and_update_without_mutating_saved_data(string defaultsJson)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        template.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var originalUpdated = template.UpdatedAt;
        db.ChangeTracker.Clear();
        var controller = CreateController(db);
        var request = new SaveResourceTemplateRequest
        {
            Name = "Changed", FormSchemaVersion = 1, FormJson = ChangedFormJson, IsActive = true,
            ResourceDefaultsJson = defaultsJson, UpdatedAt = originalUpdated,
        };

        foreach (var result in new[] { await controller.CreateTemplate(request), await controller.UpdateTemplate(template.Id, request) })
        {
            result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should()
                .Be("Resource defaults must be a valid JSON object or array.");
        }
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        saved.Name.Should().Be("Original");
        saved.FormJson.Should().Be(FormJson);
        saved.IsActive.Should().BeTrue();
        saved.UpdatedAt.Should().Be(originalUpdated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Defaults_enforce_the_character_limit_on_create_and_update(bool oversized)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = CreateController(db);
        var defaultsJson = "{\"value\":\"" + new string('x', SaveResourceTemplateRequest.MaxResourceDefaultsJsonLength - 12 + (oversized ? 1 : 0)) + "\"}";
        var request = new SaveResourceTemplateRequest
        {
            Name = "Room", FormSchemaVersion = 1, FormJson = FormJson, IsActive = true,
            ResourceDefaultsJson = defaultsJson, UpdatedAt = template.UpdatedAt,
        };

        var created = await controller.CreateTemplate(request);
        var updated = await controller.UpdateTemplate(template.Id, request);

        if (oversized)
        {
            foreach (var result in new[] { created, updated })
            {
                result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should()
                    .Be("Resource defaults cannot exceed 1,048,576 characters.");
            }
            db.ChangeTracker.HasChanges().Should().BeFalse();
            (await db.ResourceTemplates.SingleAsync()).ResourceDefaultsJson.Should().BeNull();
        }
        else
        {
            created.Result.Should().BeOfType<CreatedAtActionResult>();
            ReadOk(updated).ResourceDefaultsJson.Should().Be(defaultsJson);
            (await db.ResourceTemplates.CountAsync()).Should().Be(2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Form_changes_create_an_active_revision_and_preserve_the_archived_original_and_related_config(bool isActive)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var admin = await AddAdmin(db);
        var template = CreateTemplate("Original");
        template.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var originalCreated = template.CreatedAt;
        var originalUpdated = template.UpdatedAt;
        db.ResourceConfigs.Add(new ResourceConfig
        {
            ResourceId = 42, Version = 1, SourceTemplateId = template.Id,
            FormSchemaVersion = 1, FormJson = FormJson, DetailsJson = "{}", CreatedByUserId = admin.Id,
        });
        await db.SaveChangesAsync();
        const string reordered = """
            {"fields":[{"id":"intro","type":"text","label":"Instructions"},{"id":"name","type":"input","label":"Renamed"}]}
            """;

        var result = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = "  Edited  ", FormSchemaVersion = 1, FormJson = reordered, IsActive = isActive, UpdatedAt = originalUpdated,
        });

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<ResourceTemplateResponse>().Subject;
        response.Name.Should().Be("Edited");
        response.Id.Should().NotBe(template.Id);
        response.FormSchemaVersion.Should().Be(2);
        db.ChangeTracker.Clear();
        var saved = await db.ResourceTemplates.SingleAsync(current => current.Id == response.Id);
        saved.FormJson.Should().Be(reordered);
        saved.IsActive.Should().BeTrue();
        saved.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        saved.CreatedAt.Should().BeAfter(originalCreated);
        saved.UpdatedAt.Should().Be(saved.CreatedAt);
        saved.UpdatedAt.Should().BeAfter(originalUpdated);
        saved.UpdatedByUserId.Should().Be(admin.Id);
        var archived = await db.ResourceTemplates.SingleAsync(current => current.Id == template.Id);
        archived.Name.Should().Be("Original");
        archived.IsActive.Should().BeFalse();
        archived.FormSchemaVersion.Should().Be(1);
        archived.FormJson.Should().Be(FormJson);
        archived.CreatedAt.Should().Be(originalCreated);
        archived.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        archived.UpdatedAt.Should().Be(saved.UpdatedAt);
        archived.UpdatedByUserId.Should().Be(admin.Id);
        var config = await db.ResourceConfigs.SingleAsync();
        config.FormJson.Should().Be(FormJson);
        config.SourceTemplateId.Should().Be(template.Id);
    }

    [Fact]
    public async Task Metadata_and_json_formatting_changes_keep_the_id_version_and_original_json()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        template.FormSchemaVersion = 5;
        template.FormJson = "{\"fields\":[{\"id\":\"a\",\"type\":\"input\",\"label\":\"Name\"}]}";
        template.ResourceDefaultsJson = "{\"capacity\":12}";
        // A future timestamp verifies that the optimistic token advances even if the clock does not.
        template.UpdatedAt = DateTimeOffset.UtcNow.AddDays(1);
        var originalUpdated = template.UpdatedAt;
        var originalCreated = template.CreatedAt;
        var originalJson = template.FormJson;
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        const string reformatted = """
            {
              "fields": [ { "label": "Name", "type": "input", "id": "a", "helpText": "", "validation": { "required": false } } ]
            }
            """;

        var result = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = " Renamed ", FormSchemaVersion = 5, FormJson = reformatted, IsActive = true,
            UpdatedAt = originalUpdated,
        });

        var response = ReadOk(result);
        response.Id.Should().Be(template.Id);
        response.FormSchemaVersion.Should().Be(5);
        response.UpdatedAt.Should().Be(originalUpdated.AddTicks(1));
        response.FormJson.Should().Be(originalJson);
        db.ChangeTracker.Clear();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Name.Should().Be("Renamed");
        saved.IsActive.Should().BeTrue();
        saved.FormJson.Should().Be(originalJson);
        saved.CreatedAt.Should().Be(originalCreated);
        saved.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");

        var archived = ReadOk(await CreateController(db).UpdateTemplate(saved.Id, new SaveResourceTemplateRequest
        {
            Name = saved.Name, FormSchemaVersion = saved.FormSchemaVersion, FormJson = saved.FormJson,
            IsActive = false, UpdatedAt = response.UpdatedAt,
        }));
        archived.Id.Should().Be(saved.Id);
        archived.IsActive.Should().BeFalse();
        archived.FormSchemaVersion.Should().Be(5);
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Create_rejects_client_assigned_revision_numbers()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);

        var result = await CreateController(db).CreateTemplate(new SaveResourceTemplateRequest
        {
            Name = "Skipped revisions", FormSchemaVersion = 2, FormJson = FormJson, IsActive = true,
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.ResourceTemplates.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("rename", false)]
    [InlineData("content", false)]
    [InlineData("reactivate", false)]
    [InlineData("defaults", false)]
    [InlineData("rename", true)]
    [InlineData("content", true)]
    [InlineData("reactivate", true)]
    [InlineData("defaults", true)]
    public async Task Archived_templates_reject_all_edits_with_current_or_pre_archive_timestamps(
        string change, bool staleTimestamp)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        template.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var openedAt = template.UpdatedAt;
        var createdAt = template.CreatedAt;
        var archived = ReadOk(await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = template.Name, FormSchemaVersion = 1, FormJson = FormJson, IsActive = false, UpdatedAt = openedAt,
        }));
        var archivedBy = template.UpdatedByUserId;
        db.ChangeTracker.Clear();

        var result = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = change == "rename" ? "Changed name" : "Original",
            FormSchemaVersion = 1,
            FormJson = change == "content" ? ChangedFormJson : FormJson,
            IsActive = change == "reactivate",
            ResourceDefaultsJson = change == "defaults" ? "{\"capacity\":24}" : template.ResourceDefaultsJson,
            UpdatedAt = staleTimestamp ? openedAt : archived.UpdatedAt,
        });

        result.Result.Should().BeOfType<ConflictObjectResult>().Which.Value.Should()
            .Be("Archived templates cannot be edited. Duplicate this template to create an active copy.");
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Id.Should().Be(template.Id);
        saved.Name.Should().Be("Original");
        saved.IsActive.Should().BeFalse();
        saved.FormSchemaVersion.Should().Be(1);
        saved.FormJson.Should().Be(FormJson);
        saved.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        saved.CreatedAt.Should().Be(createdAt);
        saved.UpdatedAt.Should().Be(archived.UpdatedAt);
        saved.UpdatedByUserId.Should().Be(archivedBy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_rejects_stale_timestamp_or_revision_without_modifying_the_template(bool staleVersion)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var originalUpdated = template.UpdatedAt;

        var result = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = "Changed", FormJson = ChangedFormJson, IsActive = true,
            FormSchemaVersion = staleVersion ? 2 : 1,
            UpdatedAt = staleVersion ? originalUpdated : originalUpdated.AddTicks(-1),
        });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Name.Should().Be("Original");
        saved.FormJson.Should().Be(FormJson);
        saved.IsActive.Should().BeTrue();
        saved.UpdatedAt.Should().Be(originalUpdated);
    }

    [Fact]
    public async Task Two_edits_from_the_same_loaded_state_cannot_create_two_successors()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var request = new SaveResourceTemplateRequest
        {
            Name = "Revision 2", FormSchemaVersion = 1, FormJson = ChangedFormJson, IsActive = true,
            UpdatedAt = template.UpdatedAt,
        };

        var first = await CreateController(db).UpdateTemplate(template.Id, request);
        var second = await CreateController(db).UpdateTemplate(template.Id, request);

        first.Result.Should().BeOfType<CreatedAtActionResult>();
        second.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.ResourceTemplates.CountAsync()).Should().Be(2);
        var successor = await db.ResourceTemplates.SingleAsync(current => current.Id != template.Id);
        successor.FormSchemaVersion.Should().Be(2);
        successor.IsActive.Should().BeTrue();
        (await db.ResourceTemplates.SingleAsync(current => current.Id == template.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_update_timestamp_and_revision_overflow_do_not_change_existing_history()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Last version");
        template.FormSchemaVersion = int.MaxValue;
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();

        var missingTimestamp = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = "Changed", FormSchemaVersion = int.MaxValue, FormJson = FormJson, IsActive = true,
        });
        var overflow = await CreateController(db).UpdateTemplate(template.Id, new SaveResourceTemplateRequest
        {
            Name = "Changed", FormSchemaVersion = int.MaxValue, FormJson = ChangedFormJson, IsActive = true,
            UpdatedAt = template.UpdatedAt,
        });

        missingTimestamp.Result.Should().BeOfType<BadRequestObjectResult>();
        overflow.Result.Should().BeOfType<ConflictObjectResult>();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
        (await db.ResourceTemplates.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_makes_an_independent_active_global_copy_with_defaults_and_new_audit(bool longName)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var admin = await AddAdmin(db);
        var source = CreateTemplate(longName ? new string('x', 200) : "Original", isActive: false);
        source.FormSchemaVersion = 3;
        source.ResourceDefaultsJson = "{\"capacity\":12}";
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var originalUpdated = source.UpdatedAt;
        var started = DateTimeOffset.UtcNow;

        var result = await CreateController(db).DuplicateTemplate(source.Id);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<ResourceTemplateResponse>().Subject;
        response.Id.Should().NotBe(source.Id);
        response.Name.Should().Be(longName ? new string('x', 193) + " (copy)" : "Original (copy)");
        response.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        db.ChangeTracker.Clear();
        var copy = await db.ResourceTemplates.SingleAsync(template => template.Id == response.Id);
        copy.TeamId.Should().BeNull();
        copy.FormJson.Should().Be(FormJson);
        copy.FormSchemaVersion.Should().Be(1);
        copy.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        copy.IsActive.Should().BeTrue();
        copy.UpdatedByUserId.Should().Be(admin.Id);
        copy.CreatedAt.Should().BeOnOrAfter(started);
        copy.UpdatedAt.Should().Be(copy.CreatedAt);
        var revisedCopy = await CreateController(db).UpdateTemplate(copy.Id, new SaveResourceTemplateRequest
        {
            Name = "Changed copy", FormSchemaVersion = 1, FormJson = ChangedFormJson, IsActive = true,
            ResourceDefaultsJson = "{\"capacity\":24}",
            UpdatedAt = copy.UpdatedAt,
        });
        revisedCopy.Result.Should().BeOfType<CreatedAtActionResult>();
        var unchanged = await db.ResourceTemplates.SingleAsync(template => template.Id == source.Id);
        unchanged.FormJson.Should().Be(FormJson);
        unchanged.FormSchemaVersion.Should().Be(3);
        unchanged.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        unchanged.IsActive.Should().BeFalse();
        unchanged.UpdatedAt.Should().Be(originalUpdated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_update_and_duplicate_cannot_access_team_owned_or_missing_ids(bool teamOwned)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var teamTemplate = CreateTemplate("Team template", teamId: 7);
        db.ResourceTemplates.Add(teamTemplate);
        await db.SaveChangesAsync();
        var id = teamOwned ? teamTemplate.Id : teamTemplate.Id + 100;
        db.ChangeTracker.Clear();
        var controller = CreateController(db);

        (await controller.GetTemplate(id)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.UpdateTemplate(id, CreateRequest("Changed"))).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.DuplicateTemplate(id)).Result.Should().BeOfType<NotFoundObjectResult>();

        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
        (await db.ResourceTemplates.SingleAsync()).Name.Should().Be("Team template");
    }

    [Fact]
    public async Task Invalid_create_and_update_do_not_save_or_mutate_the_existing_template()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = CreateController(db);
        SaveResourceTemplateRequest[] invalidRequests =
        [
            CreateRequest("  "),
            CreateRequest(new string('x', 201)),
            new() { Name = "Name", FormSchemaVersion = 0, FormJson = FormJson },
            new() { Name = "Name", FormSchemaVersion = 1, FormJson = "{invalid}" },
            new() { Name = "Name", FormSchemaVersion = 1, FormJson = "{\"fields\":[null]}" },
        ];

        foreach (var request in invalidRequests)
        {
            (await controller.CreateTemplate(request)).Result.Should().BeOfType<BadRequestObjectResult>();
            (await controller.UpdateTemplate(template.Id, request)).Result.Should().BeOfType<BadRequestObjectResult>();
        }
        (await controller.CreateTemplate(null!)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateTemplate(template.Id, null!)).Result.Should().BeOfType<BadRequestObjectResult>();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
        (await db.ResourceTemplates.SingleAsync()).Name.Should().Be("Original");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_forms_cannot_be_created_or_saved(bool isActive)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var template = CreateTemplate("Original");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var originalUpdated = template.UpdatedAt;
        db.ChangeTracker.Clear();
        var controller = CreateController(db);
        var request = new SaveResourceTemplateRequest
        {
            Name = "Empty form", FormSchemaVersion = 1, FormJson = EmptyFormJson,
            IsActive = isActive, UpdatedAt = originalUpdated,
        };

        var created = await controller.CreateTemplate(request);
        var updated = await controller.UpdateTemplate(template.Id, request);

        created.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should()
            .Be("Add at least one form field before saving the template.");
        updated.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should()
            .Be("Add at least one form field before saving the template.");
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Name.Should().Be("Original");
        saved.FormJson.Should().Be(FormJson);
        saved.IsActive.Should().BeTrue();
        saved.UpdatedAt.Should().Be(originalUpdated);
    }

    [Fact]
    public async Task Duplicate_rejects_an_empty_saved_form_without_creating_a_copy()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Empty form");
        source.FormJson = EmptyFormJson;
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).DuplicateTemplate(source.Id);

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should()
            .Be("Add at least one form field before saving the template.");
        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.ResourceTemplates.SingleAsync()).FormJson.Should().Be(source.FormJson);
    }

    [Fact]
    public async Task An_existing_empty_form_can_be_opened_and_repaired()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Empty form");
        source.FormJson = EmptyFormJson;
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        ReadOk(await controller.GetTemplate(source.Id)).FormJson.Should().Be(source.FormJson);
        var result = await controller.UpdateTemplate(source.Id, new SaveResourceTemplateRequest
        {
            Name = source.Name, FormSchemaVersion = 1, FormJson = FormJson, IsActive = true,
            UpdatedAt = source.UpdatedAt,
        });

        var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<ResourceTemplateResponse>().Subject;
        response.FormJson.Should().Be(FormJson);
        response.FormSchemaVersion.Should().Be(2);
        response.IsActive.Should().BeTrue();
        (await db.ResourceTemplates.CountAsync()).Should().Be(2);
        var archived = await db.ResourceTemplates.SingleAsync(template => template.Id == source.Id);
        archived.FormJson.Should().Be(EmptyFormJson);
        archived.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_single_text_block_can_be_created_saved_and_duplicated()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Original");
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var request = new SaveResourceTemplateRequest
        {
            Name = "Instructions", FormSchemaVersion = 1, FormJson = TextOnlyFormJson, IsActive = true,
            UpdatedAt = source.UpdatedAt,
        };

        var created = await controller.CreateTemplate(request);
        var updated = await controller.UpdateTemplate(source.Id, request);
        var revision = updated.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<ResourceTemplateResponse>().Subject;
        var duplicated = await controller.DuplicateTemplate(revision.Id);

        foreach (var result in new[] { created, updated, duplicated })
        {
            var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
                .Should().BeOfType<ResourceTemplateResponse>().Subject;
            response.FormJson.Should().Be(TextOnlyFormJson);
            response.IsActive.Should().BeTrue();
        }
        (await db.ResourceTemplates.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Duplicate_rejects_an_unsupported_saved_form_without_creating_a_copy()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Future form");
        source.FormSchemaVersion = 2;
        source.FormJson = "{\"fields\":[],\"futureFeature\":true}";
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();

        (await CreateController(db).DuplicateTemplate(source.Id)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Update_rejects_unknown_saved_content_without_changing_it()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Future form");
        source.FormSchemaVersion = 2;
        source.FormJson = "{\"fields\":[],\"futureFeature\":true}";
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var originalUpdated = source.UpdatedAt;
        var originalUpdatedBy = source.UpdatedByUserId;
        db.ChangeTracker.Clear();

        var result = await CreateController(db).UpdateTemplate(source.Id, new SaveResourceTemplateRequest
        {
            Name = "Changed form", FormSchemaVersion = 2, FormJson = FormJson, IsActive = true,
            UpdatedAt = source.UpdatedAt,
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Name.Should().Be("Future form");
        saved.FormSchemaVersion.Should().Be(2);
        saved.FormJson.Should().Be("{\"fields\":[],\"futureFeature\":true}");
        saved.IsActive.Should().BeTrue();
        saved.UpdatedAt.Should().Be(originalUpdated);
        saved.UpdatedByUserId.Should().Be(originalUpdatedBy);
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("missing", true, true)]
    [InlineData("current-admin", false, true)]
    [InlineData("current-admin", true, false)]
    public async Task Mutations_require_an_effective_active_admin_and_do_not_use_the_emulation_actor(
        string? iamId, bool isActive, bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var admin = await AddAdmin(db);
        admin.IsActive = isActive;
        admin.IsAdmin = isAdmin;
        var template = CreateTemplate("Original");
        db.ResourceTemplates.Add(template);
        await db.SaveChangesAsync();
        var controller = CreateController(db, iamId);
        controller.HttpContext.Items[EmulationService.ActorItemKey] = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("ucdPersonIAMID", "current-admin")], "Test"));

        (await controller.CreateTemplate(CreateRequest("New"))).Result.Should().BeOfType<ForbidResult>();
        (await controller.UpdateTemplate(template.Id, CreateRequest("Changed"))).Result.Should().BeOfType<ForbidResult>();
        (await controller.DuplicateTemplate(template.Id)).Result.Should().BeOfType<ForbidResult>();
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
        (await db.ResourceTemplates.SingleAsync()).Name.Should().Be("Original");
    }

    [Fact]
    public void Controller_requires_site_admin_authorization_and_antiforgery_on_every_write()
    {
        var controller = typeof(AdminResourceTemplatesController);
        controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Should().Contain(attribute => attribute.Policy == AuthenticationHelper.SiteAdminPolicy);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/admin/resource-templates");
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        controller.GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>(inherit: true).Should().NotBeNull();
        controller.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            method.GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
            method.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        }
        controller.GetMethod(nameof(AdminResourceTemplatesController.CreateTemplate))!
            .GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
        controller.GetMethod(nameof(AdminResourceTemplatesController.UpdateTemplate))!
            .GetCustomAttribute<HttpPutAttribute>()!.Template.Should().Be("{id:int}");
        controller.GetMethod(nameof(AdminResourceTemplatesController.DuplicateTemplate))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("{id:int}/duplicate");
    }

    [Theory]
    [InlineData("teamId", "7")]
    [InlineData("updatedByUserId", "7")]
    public void Save_contract_rejects_fields_outside_the_editable_template(string property, string value)
    {
        var json = "{\"name\":\"Name\",\"formSchemaVersion\":1,\"formJson\":\"{\\\"fields\\\":[]}\",\"isActive\":true,\"" +
            property + "\":" + value + "}";

        var deserialize = () => JsonSerializer.Deserialize<SaveResourceTemplateRequest>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        deserialize.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("", false, null)]
    [InlineData(",\"resourceDefaultsJson\":null", true, null)]
    [InlineData(",\"resourceDefaultsJson\":\"{}\"", true, "{}")]
    public void Save_contract_distinguishes_omitted_defaults_from_explicit_values(string defaultsProperty, bool isPresent, string? expected)
    {
        var json = "{\"name\":\"Room\",\"formSchemaVersion\":1,\"formJson\":\"{\\\"fields\\\":[]}\",\"isActive\":true" + defaultsProperty + "}";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var request = JsonSerializer.Deserialize<SaveResourceTemplateRequest>(json, options)!;

        request.HasResourceDefaultsJson.Should().Be(isPresent);
        request.ResourceDefaultsJson.Should().Be(expected);
        JsonSerializer.Serialize(request, options).Should().NotContain("hasResourceDefaultsJson");
    }

    [Fact]
    public void Save_contract_requires_explicit_active_status_instead_of_defaulting_to_reactivation()
    {
        const string json = """
            {"name":"Archived form","formSchemaVersion":1,"formJson":"{\"fields\":[]}"}
            """;

        var deserialize = () => JsonSerializer.Deserialize<SaveResourceTemplateRequest>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        deserialize.Should().Throw<JsonException>();
    }

    private static T ReadOk<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static SaveResourceTemplateRequest CreateRequest(string name, bool isActive = true) => new()
    {
        Name = name, FormSchemaVersion = 1, FormJson = FormJson, IsActive = isActive,
    };

    private static ResourceTemplate CreateTemplate(string name, int? teamId = null, bool isActive = true) => new()
    {
        Name = name, TeamId = teamId, FormSchemaVersion = 1, FormJson = FormJson, IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1), UpdatedByUserId = 99,
    };

    private static async Task<User> AddAdmin(AppDbContext db)
    {
        var admin = new User { IamId = "current-admin", Name = "Current admin", IsAdmin = true };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    private static AdminResourceTemplatesController CreateController(AppDbContext db, string? iamId = "current-admin")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "unrelated-identity-id") };
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new AdminResourceTemplatesController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }
}

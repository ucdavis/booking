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

    [Fact]
    public async Task Update_preserves_defaults_creation_and_related_config_while_saving_form_order_and_status()
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
            Name = "  Edited  ", FormSchemaVersion = 1, FormJson = reordered, IsActive = false,
        });

        ReadOk(result).Name.Should().Be("Edited");
        db.ChangeTracker.Clear();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.FormJson.Should().Be(reordered);
        saved.IsActive.Should().BeFalse();
        saved.ResourceDefaultsJson.Should().Be("{\"capacity\":12}");
        saved.CreatedAt.Should().Be(originalCreated);
        saved.UpdatedAt.Should().BeAfter(originalUpdated);
        saved.UpdatedByUserId.Should().Be(admin.Id);
        (await db.ResourceConfigs.SingleAsync()).FormJson.Should().Be(FormJson);

        await CreateController(db).UpdateTemplate(template.Id, CreateRequest("Reactivated"));
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
        await CreateController(db).UpdateTemplate(copy.Id, new SaveResourceTemplateRequest
        {
            Name = "Changed copy", FormSchemaVersion = 1, FormJson = "{\"fields\":[]}", IsActive = true,
        });
        var unchanged = await db.ResourceTemplates.SingleAsync(template => template.Id == source.Id);
        unchanged.FormJson.Should().Be(FormJson);
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
            new() { Name = "Name", FormSchemaVersion = 2, FormJson = FormJson },
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

    [Fact]
    public async Task Duplicate_rejects_an_unsupported_saved_form_without_creating_a_copy()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Future form");
        source.FormSchemaVersion = 2;
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();

        (await CreateController(db).DuplicateTemplate(source.Id)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.ResourceTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Update_rejects_downgrading_an_existing_unsupported_form_without_changing_it()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        await AddAdmin(db);
        var source = CreateTemplate("Future form", isActive: false);
        source.FormSchemaVersion = 2;
        source.FormJson = "{\"fields\":[],\"futureFeature\":true}";
        db.ResourceTemplates.Add(source);
        await db.SaveChangesAsync();
        var originalUpdated = source.UpdatedAt;
        var originalUpdatedBy = source.UpdatedByUserId;
        db.ChangeTracker.Clear();

        var result = await CreateController(db).UpdateTemplate(source.Id, CreateRequest("Downgraded form"));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.ResourceTemplates.SingleAsync();
        saved.Name.Should().Be("Future form");
        saved.FormSchemaVersion.Should().Be(2);
        saved.FormJson.Should().Be("{\"fields\":[],\"futureFeature\":true}");
        saved.IsActive.Should().BeFalse();
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
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            method.GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
        }
        foreach (var methodName in new[]
        {
            nameof(AdminResourceTemplatesController.CreateTemplate),
            nameof(AdminResourceTemplatesController.UpdateTemplate),
            nameof(AdminResourceTemplatesController.DuplicateTemplate),
        })
        {
            controller.GetMethod(methodName)!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>()
                .Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData("teamId", "7")]
    [InlineData("updatedByUserId", "7")]
    [InlineData("resourceDefaultsJson", "\"{}\"")]
    public void Save_contract_rejects_fields_outside_the_editable_form(string property, string value)
    {
        var json = "{\"name\":\"Name\",\"formSchemaVersion\":1,\"formJson\":\"{\\\"fields\\\":[]}\",\"isActive\":true,\"" +
            property + "\":" + value + "}";

        var deserialize = () => JsonSerializer.Deserialize<SaveResourceTemplateRequest>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        deserialize.Should().Throw<JsonException>();
        typeof(ResourceTemplateResponse).GetProperty("ResourceDefaultsJson").Should().BeNull();
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

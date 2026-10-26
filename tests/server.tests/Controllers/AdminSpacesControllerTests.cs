using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Spaces;

namespace Server.Tests.Controllers;

public class AdminSpacesControllerTests
{
    [Fact]
    public async Task Reads_all_spaces_in_name_and_slug_order_without_tracking()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Spaces.AddRange(
            CreateSpace("West Hall", "west-hall"),
            CreateSpace("Arboretum", "arboretum-z"),
            CreateSpace("Arboretum", "arboretum-a", isActive: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new AdminSpacesController(db);

        var spaces = ReadOk(await controller.GetSpaces());

        spaces.Select(space => space.Slug).Should().Equal("arboretum-a", "arboretum-z", "west-hall");
        spaces[0].IsActive.Should().BeFalse();
        ReadOk(await controller.GetSpace(spaces[0].Id)).Name.Should().Be("Arboretum");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_saves_trimmed_identity_with_Pacific_time_without_team_links()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var started = DateTimeOffset.UtcNow;

        var result = await new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest
        {
            Name = "  Outdoor Lawn  ", Slug = " outdoor-lawn ",
        });

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<SpaceResponse>().Subject;
        created.ActionName.Should().Be(nameof(AdminSpacesController.GetSpace));
        created.RouteValues!["id"].Should().Be(response.Id);
        db.ChangeTracker.Clear();
        var space = await db.Spaces.SingleAsync();
        response.Id.Should().Be(space.Id);
        response.Name.Should().Be("Outdoor Lawn");
        response.Slug.Should().Be("outdoor-lawn");
        space.TimeZoneId.Should().Be("America/Los_Angeles");
        space.IsActive.Should().BeTrue();
        space.IsOfficialFacility.Should().BeFalse();
        space.CreatedAt.Should().BeOnOrAfter(started);
        space.UpdatedAt.Should().Be(space.CreatedAt);
        (await db.TeamSpaces.AnyAsync()).Should().BeFalse();
        (await db.Resources.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Create_and_reads_include_trimmed_metadata_and_preserve_explicit_inactive_status()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = new AdminSpacesController(db);

        var result = await controller.CreateSpace(new SaveSpaceRequest
        {
            Name = "  North Hall  ", Description = "  A teaching space.  ", Address = "  123 Main St  ",
            IsOfficialFacility = true, IsActive = false, ReferenceNumber = "  FAC-123  ",
        });

        var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<SpaceResponse>().Subject;
        response.Description.Should().Be("A teaching space.");
        response.Address.Should().Be("123 Main St");
        response.IsOfficialFacility.Should().BeTrue();
        response.IsActive.Should().BeFalse();
        response.ReferenceNumber.Should().Be("FAC-123");
        db.ChangeTracker.Clear();
        var saved = await db.Spaces.SingleAsync();
        saved.Description.Should().Be(response.Description);
        saved.Address.Should().Be(response.Address);
        saved.IsOfficialFacility.Should().BeTrue();
        saved.IsActive.Should().BeFalse();
        saved.ReferenceNumber.Should().Be(response.ReferenceNumber);
        saved.TimeZoneId.Should().Be("America/Los_Angeles");
        ReadOk(await controller.GetSpace(saved.Id)).Should().BeEquivalentTo(response);
        ReadOk(await controller.GetSpaces()).Should().ContainSingle().Which.Should().BeEquivalentTo(response);
        JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().NotContain("timeZoneId");
    }

    [Fact]
    public void SqlServer_model_preserves_explicit_inactive_status_on_insert()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(local);Database=BookingModelTests;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new AppDbContext(options);
        var property = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Space))!
            .FindProperty(nameof(Space.IsActive))!;

        property.GetDefaultValue().Should().Be(true);
        property.Sentinel.Should().Be(true, "an explicit false must be sent to SQL Server instead of using its true default");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Official_facilities_require_a_reference_on_create_and_update(string? referenceNumber)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var original = CreateSpace("Original Hall", "original-hall");
        db.Spaces.Add(original);
        await db.SaveChangesAsync();
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest
        {
            Name = "Official Hall", IsOfficialFacility = true, ReferenceNumber = referenceNumber,
        };

        (await controller.CreateSpace(request)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateSpace(original.Id, request)).Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.Spaces.SingleAsync()).IsOfficialFacility.Should().BeFalse();
    }

    [Theory]
    [InlineData(501, 100)]
    [InlineData(500, 101)]
    public async Task Oversized_address_and_reference_are_rejected_on_create_and_update(int addressLength, int referenceLength)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var original = CreateSpace("Original Hall", "original-hall");
        db.Spaces.Add(original);
        await db.SaveChangesAsync();
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest
        {
            Name = "Updated Hall", Address = new string('a', addressLength), ReferenceNumber = new string('a', referenceLength),
        };

        (await controller.CreateSpace(request)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateSpace(original.Id, request)).Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData("North Hall", "north-hall")]
    [InlineData("  North   Hall  ", "north-hall")]
    [InlineData("Café & Conference Hall", "cafe-conference-hall")]
    [InlineData("Ｆｕｌｌ Width", "full-width")]
    public async Task Create_defaults_an_omitted_slug_from_the_name(string name, string expectedSlug)
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest { Name = name });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        (await db.Spaces.SingleAsync()).Slug.Should().Be(expectedSlug);
    }

    [Fact]
    public async Task Generated_slug_respects_its_limit_without_ending_in_a_hyphen()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var name = new string('a', 119) + " West Wing";

        var result = await new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest { Name = name });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        (await db.Spaces.SingleAsync()).Slug.Should().Be(new string('a', 119));
    }

    [Theory]
    [InlineData("", "hall")]
    [InlineData("   ", "hall")]
    [InlineData("North\nHall", "north-hall")]
    [InlineData("Hall", "")]
    [InlineData("Hall", "   ")]
    [InlineData("Hall", "Hall")]
    [InlineData("Hall", "-hall")]
    [InlineData("Hall", "hall-")]
    [InlineData("Hall", "north--hall")]
    [InlineData("Hall", "north_hall")]
    [InlineData("Hall", "north/hall")]
    [InlineData("Hall", "háll")]
    public async Task Create_and_update_reject_invalid_identity_without_changes(string name, string? slug)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var original = CreateSpace("Original", "original");
        db.Spaces.Add(original);
        await db.SaveChangesAsync();
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest { Name = name, Slug = slug };

        (await controller.CreateSpace(request)).Result.Should().BeOfType<BadRequestObjectResult>();
        (await controller.UpdateSpace(original.Id, request)).Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.HasChanges().Should().BeFalse();
        var saved = await db.Spaces.SingleAsync();
        saved.Name.Should().Be("Original");
        saved.Slug.Should().Be("original");
    }

    [Fact]
    public async Task Create_rejects_a_name_without_an_automatic_slug()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest { Name = "???" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Spaces.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(201, 120)]
    [InlineData(200, 121)]
    public async Task Create_rejects_oversized_names_or_slugs(int nameLength, int slugLength)
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest
        {
            Name = new string('a', nameLength), Slug = new string('a', slugLength),
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Spaces.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Create_accepts_maximum_lengths()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest
        {
            Name = new string('a', 200), Slug = new string('a', 120),
            Address = new string('a', 500), ReferenceNumber = new string('a', 100), IsOfficialFacility = true,
        });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Update_changes_editable_fields_preserving_slug_timezone_and_creation_timestamp()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var original = CreateSpace("Old Hall", "old-hall", isActive: false);
        original.TimeZoneId = "America/New_York";
        original.Description = "Existing description";
        original.Address = "Existing address";
        original.IsOfficialFacility = true;
        original.ReferenceNumber = "12345";
        db.Spaces.Add(original);
        await db.SaveChangesAsync();
        var createdAt = original.CreatedAt;
        var updatedAt = original.UpdatedAt;
        db.ChangeTracker.Clear();

        var response = ReadOk(await new AdminSpacesController(db).UpdateSpace(original.Id,
            new SaveSpaceRequest
            {
                Name = "  New Hall  ", Description = "  Updated description  ", Address = "  Updated address  ",
                IsOfficialFacility = false, IsActive = true, ReferenceNumber = "  67890  ",
            }));

        response.Name.Should().Be("New Hall");
        response.Slug.Should().Be("old-hall");
        response.Description.Should().Be("Updated description");
        response.Address.Should().Be("Updated address");
        response.IsOfficialFacility.Should().BeFalse();
        response.IsActive.Should().BeTrue();
        response.ReferenceNumber.Should().Be("67890");
        db.ChangeTracker.Clear();
        var saved = await db.Spaces.SingleAsync();
        saved.TimeZoneId.Should().Be("America/New_York");
        saved.Slug.Should().Be("old-hall");
        saved.Description.Should().Be("Updated description");
        saved.Address.Should().Be("Updated address");
        saved.IsOfficialFacility.Should().BeFalse();
        saved.ReferenceNumber.Should().Be("67890");
        saved.IsActive.Should().BeTrue();
        saved.CreatedAt.Should().Be(createdAt);
        saved.UpdatedAt.Should().BeAfter(updatedAt);
        (await db.TeamSpaces.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Update_accepts_the_spaces_existing_slug()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var space = CreateSpace("Hall", "hall");
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var response = ReadOk(await new AdminSpacesController(db).UpdateSpace(space.Id,
            new SaveSpaceRequest { Name = "Renamed Hall", Slug = "hall" }));

        response.Name.Should().Be("Renamed Hall");
        response.Slug.Should().Be("hall");
    }

    [Theory]
    [InlineData("renamed-hall")]
    [InlineData("HALL")]
    [InlineData(" hall ")]
    [InlineData("")]
    public async Task Update_rejects_slug_changes_before_mutating_any_fields(string slug)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var space = CreateSpace("Hall", "hall");
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var result = await new AdminSpacesController(db).UpdateSpace(space.Id, new SaveSpaceRequest
        {
            Name = "Renamed Hall", Slug = slug, Description = "Changed", IsActive = false,
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        db.ChangeTracker.HasChanges().Should().BeFalse();
        space.Name.Should().Be("Hall");
        space.Slug.Should().Be("hall");
        space.Description.Should().BeNull();
        space.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_clears_blank_optional_fields_and_preserves_an_omitted_slug(string? value)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var space = CreateSpace("Hall", "hall");
        space.Description = "Description";
        space.Address = "Address";
        space.IsOfficialFacility = true;
        space.ReferenceNumber = "FAC-123";
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        var response = ReadOk(await new AdminSpacesController(db).UpdateSpace(space.Id, new SaveSpaceRequest
        {
            Name = "???", Description = value, Address = value, ReferenceNumber = value,
            IsOfficialFacility = false, IsActive = false,
        }));

        response.Name.Should().Be("???");
        response.Slug.Should().Be("hall");
        response.Description.Should().BeNull();
        response.Address.Should().BeNull();
        response.ReferenceNumber.Should().BeNull();
        response.IsOfficialFacility.Should().BeFalse();
        response.IsActive.Should().BeFalse();
        db.ChangeTracker.Clear();
        var saved = await db.Spaces.SingleAsync();
        saved.Slug.Should().Be("hall");
        saved.Description.Should().BeNull();
        saved.Address.Should().BeNull();
        saved.ReferenceNumber.Should().BeNull();
        saved.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Official_reference_conflicts_include_inactive_facilities_and_do_not_change_existing_spaces()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var official = CreateSpace("Existing Hall", "existing-hall", isActive: false);
        official.IsOfficialFacility = true;
        official.ReferenceNumber = "FAC-123";
        var original = CreateSpace("Original Hall", "original-hall");
        db.Spaces.AddRange(official, original);
        await db.SaveChangesAsync();
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest
        {
            Name = "Replacement", IsOfficialFacility = true, ReferenceNumber = "  FAC-123  ",
        };

        var conflict = ReadConflict(await controller.CreateSpace(request));
        conflict.Status.Should().Be(409);
        conflict.Extensions["code"].Should().Be("duplicate_reference_number");
        conflict.Detail.Should().Contain("reference number");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(conflict));
        json.RootElement.GetProperty("code").GetString().Should().Be("duplicate_reference_number");
        ReadConflict(await controller.UpdateSpace(original.Id, request)).Extensions["code"]
            .Should().Be("duplicate_reference_number");

        db.ChangeTracker.HasChanges().Should().BeFalse();
        original.Name.Should().Be("Original Hall");
        original.IsOfficialFacility.Should().BeFalse();
    }

    [Fact]
    public async Task Nonofficial_spaces_can_retain_duplicate_references_and_official_spaces_keep_their_own_reference()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var official = CreateSpace("Official Hall", "official-hall");
        official.IsOfficialFacility = true;
        official.ReferenceNumber = "FAC-123";
        db.Spaces.Add(official);
        await db.SaveChangesAsync();
        var controller = new AdminSpacesController(db);

        var result = await controller.CreateSpace(new SaveSpaceRequest
        {
            Name = "Unofficial Space", ReferenceNumber = "  FAC-123  ",
        });
        var response = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<SpaceResponse>().Subject;
        response.IsOfficialFacility.Should().BeFalse();
        response.ReferenceNumber.Should().Be("FAC-123");
        ReadOk(await controller.UpdateSpace(official.Id, new SaveSpaceRequest
        {
            Name = "Renamed Official Hall", IsOfficialFacility = true, ReferenceNumber = "FAC-123",
        })).ReferenceNumber.Should().Be("FAC-123");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_official_reference_conflict_discards_unsaved_create_and_update_changes(bool update)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SpaceReferenceConflict_{Guid.NewGuid():N}").Options;
        var original = CreateSpace("Original Hall", "original-hall");
        using (var seedDb = new AppDbContext(options))
        {
            seedDb.Spaces.Add(original);
            await seedDb.SaveChangesAsync();
        }
        using var db = new SpaceSaveFailureDbContext(options, "duplicate_reference_number");
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest
        {
            Name = "Candidate Hall", IsOfficialFacility = true, ReferenceNumber = "FAC-123", IsActive = false,
        };

        var result = update
            ? await controller.UpdateSpace(original.Id, request)
            : await controller.CreateSpace(request);

        ReadConflict(result).Extensions["code"].Should().Be("duplicate_reference_number");
        db.ChangeTracker.HasChanges().Should().BeFalse();
        var savedOriginal = await db.Spaces.SingleAsync(space => space.Id == original.Id);
        savedOriginal.Name.Should().Be("Original Hall");
        savedOriginal.IsOfficialFacility.Should().BeFalse();
        savedOriginal.IsActive.Should().BeTrue();
        (await db.Spaces.SingleAsync(space => space.IsOfficialFacility)).ReferenceNumber.Should().Be("FAC-123");
    }

    [Fact]
    public async Task Read_and_update_return_not_found_for_a_missing_space()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var controller = new AdminSpacesController(db);

        (await controller.GetSpace(123)).Result.Should().BeOfType<NotFoundObjectResult>();
        (await controller.UpdateSpace(123, new SaveSpaceRequest { Name = "Hall", Slug = "hall" }))
            .Result.Should().BeOfType<NotFoundObjectResult>();

        (await db.Spaces.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_slug_returns_conflict_without_changing_existing_spaces()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var original = CreateSpace("Original Hall", "original-hall");
        db.Spaces.AddRange(original, CreateSpace("Existing Hall", "existing-hall", isActive: false));
        await db.SaveChangesAsync();
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest { Name = "Replacement", Slug = "existing-hall" };

        ReadConflict(await controller.CreateSpace(request)).Extensions["code"].Should().Be("duplicate_slug");
        (await controller.UpdateSpace(original.Id, request)).Result.Should().BeOfType<BadRequestObjectResult>();

        db.ChangeTracker.HasChanges().Should().BeFalse();
        original.Name.Should().Be("Original Hall");
        original.Slug.Should().Be("original-hall");
        (await db.Spaces.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_duplicate_slug_returns_conflict_and_discards_unsaved_changes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SpaceConflict_{Guid.NewGuid():N}").Options;
        var original = CreateSpace("Original Hall", "original-hall");
        using (var seedDb = new AppDbContext(options))
        {
            seedDb.Spaces.Add(original);
            await seedDb.SaveChangesAsync();
        }
        using var db = new SpaceSaveFailureDbContext(options, "duplicate_slug");
        var controller = new AdminSpacesController(db);
        var request = new SaveSpaceRequest { Name = "Candidate", Slug = "concurrent-hall" };

        var result = await controller.CreateSpace(request);

        ReadConflict(result).Extensions["code"].Should().Be("duplicate_slug");
        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.Spaces.SingleAsync(space => space.Id == original.Id)).Name.Should().Be("Original Hall");
        (await db.Spaces.SingleAsync(space => space.Slug == "concurrent-hall")).Name.Should().Be("Concurrent Hall");
    }

    [Fact]
    public async Task Unrelated_persistence_errors_are_not_reported_as_duplicate_slugs()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SpaceFailure_{Guid.NewGuid():N}").Options;
        using var db = new SpaceSaveFailureDbContext(options, conflictCode: null);

        var create = () => new AdminSpacesController(db).CreateSpace(new SaveSpaceRequest { Name = "Hall", Slug = "hall" });

        await create.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData("timeZoneId", "\"UTC\"")]
    [InlineData("teamId", "7")]
    public void Save_contract_rejects_fields_outside_editable_space_metadata(string property, string value)
    {
        var json = "{\"name\":\"Hall\",\"slug\":\"hall\",\"" + property + "\":" + value + "}";

        var deserialize = () => JsonSerializer.Deserialize<SaveSpaceRequest>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        deserialize.Should().Throw<JsonException>();
    }

    [Fact]
    public void Controller_requires_site_admin_authorization_and_antiforgery_on_writes()
    {
        var controller = typeof(AdminSpacesController);
        controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Should().Contain(attribute => attribute.Policy == AuthenticationHelper.SiteAdminPolicy);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/admin/spaces");
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>(inherit: true).Should().NotBeNull();
        controller.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            method.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
            method.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        }
        controller.GetMethod(nameof(AdminSpacesController.CreateSpace))!
            .GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
        controller.GetMethod(nameof(AdminSpacesController.UpdateSpace))!
            .GetCustomAttribute<HttpPutAttribute>()!.Template.Should().Be("{id:int}");
    }

    private static T ReadOk<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static ProblemDetails ReadConflict(ActionResult<SpaceResponse> result)
        => result.Result.Should().BeOfType<ConflictObjectResult>().Subject.Value.Should().BeOfType<ProblemDetails>().Subject;

    private static Space CreateSpace(string name, string slug, bool isActive = true) => new()
    {
        Name = name, Slug = slug, TimeZoneId = "America/Los_Angeles", IsActive = isActive,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private sealed class SpaceSaveFailureDbContext : AppDbContext
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly string? _conflictCode;

        public SpaceSaveFailureDbContext(DbContextOptions<AppDbContext> options, string? conflictCode)
            : base(options)
        {
            _options = options;
            _conflictCode = conflictCode;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var candidate = ChangeTracker.Entries<Space>()
                .Single(entry => entry.State == EntityState.Added || entry.State == EntityState.Modified);
            if (_conflictCode != null)
            {
                using var concurrentDb = new AppDbContext(_options);
                var concurrentSpace = CreateSpace("Concurrent Hall",
                    _conflictCode == "duplicate_slug" ? candidate.Entity.Slug : "concurrent-hall");
                if (_conflictCode == "duplicate_reference_number")
                {
                    concurrentSpace.IsOfficialFacility = true;
                    concurrentSpace.ReferenceNumber = candidate.Entity.ReferenceNumber;
                }
                concurrentDb.Spaces.Add(concurrentSpace);
                await concurrentDb.SaveChangesAsync(cancellationToken);
            }

            throw new DbUpdateException("Simulated space persistence failure.");
        }
    }
}

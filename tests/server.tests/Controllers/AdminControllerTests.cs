using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Admin;
using Server.Models.Directory;
using Server.Models.Teams;

namespace Server.Tests.Controllers;

public class AdminControllerTests
{
    private readonly List<DirectoryPerson> _people = [];

    [Fact]
    public async Task GetTeams_returns_all_teams_in_name_and_slug_order()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.AddRange(
            new Team { Name = "Zoology", Slug = "zoology" },
            new Team { Name = "Biology", Slug = "biology-z" },
            new Team { Name = "Biology", Slug = "biology-a" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var teams = ReadValue<List<TeamSummaryResponse>>(await CreateController(db).GetTeams());

        teams.Select(team => team.Slug).Should().Equal("biology-a", "biology-z", "zoology");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTeam_saves_trimmed_identity_and_timestamps_without_assigning_membership()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var started = DateTimeOffset.UtcNow;

        var result = await CreateController(db).CreateTeam(new CreateTeamRequest
        {
            Name = "  Biology  ", Slug = " biology-2 ",
        });

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = created.Value.Should().BeOfType<TeamSummaryResponse>().Subject;
        created.ActionName.Should().Be(nameof(TeamsController.GetTeam));
        created.ControllerName.Should().Be("Teams");
        created.RouteValues!["teamSlug"].Should().Be("biology-2");
        db.ChangeTracker.Clear();
        var team = await db.Teams.SingleAsync();
        response.Id.Should().Be(team.Id);
        response.Name.Should().Be("Biology");
        response.Slug.Should().Be("biology-2");
        team.CreatedAt.Should().BeOnOrAfter(started);
        team.UpdatedAt.Should().Be(team.CreatedAt);
        team.PaymentsTeamSlug.Should().BeNull();
        team.PaymentsApiKeySecretName.Should().BeNull();
        (await db.TeamPermissions.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "biology")]
    [InlineData("   ", "biology")]
    [InlineData("Biology", "")]
    [InlineData("Biology", "   ")]
    [InlineData("Biology", "Biology")]
    [InlineData("Biology", "-biology")]
    [InlineData("Biology", "biology-")]
    [InlineData("Biology", "biology--labs")]
    [InlineData("Biology", "biology_labs")]
    [InlineData("Biology", "biology/labs")]
    [InlineData("Biology", "biología")]
    public async Task CreateTeam_rejects_invalid_names_and_slugs_without_saving(string name, string slug)
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).CreateTeam(new CreateTeamRequest { Name = name, Slug = slug });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Teams.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(201, 100)]
    [InlineData(200, 101)]
    public async Task CreateTeam_rejects_oversized_names_or_slugs(int nameLength, int slugLength)
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).CreateTeam(new CreateTeamRequest
        {
            Name = new string('a', nameLength), Slug = new string('a', slugLength),
        });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Teams.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task CreateTeam_accepts_maximum_lengths()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).CreateTeam(new CreateTeamRequest
        {
            Name = new string('a', 200), Slug = new string('a', 100),
        });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task CreateTeam_returns_a_conflict_without_changing_the_existing_team()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Teams.Add(new Team { Name = "Original Name", Slug = "biology" });
        await db.SaveChangesAsync();

        var result = await CreateController(db).CreateTeam(new CreateTeamRequest { Name = "New Name", Slug = "biology" });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.Teams.SingleAsync()).Name.Should().Be("Original Name");
    }

    [Fact]
    public async Task CreateTeam_returns_a_conflict_when_another_request_inserts_the_slug()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TeamRace_{Guid.NewGuid():N}").Options;
        using var db = new TeamInsertFailureDbContext(options, insertConcurrentTeam: true);

        var result = await CreateController(db).CreateTeam(new CreateTeamRequest { Name = "Biology", Slug = "biology" });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        (await db.Teams.SingleAsync()).Name.Should().Be("Concurrent Team");
        db.ChangeTracker.Entries<Team>().Should().OnlyContain(entry => entry.State != EntityState.Added);
    }

    [Fact]
    public async Task CreateTeam_does_not_report_unrelated_save_failures_as_duplicate_slugs()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TeamFailure_{Guid.NewGuid():N}").Options;
        using var db = new TeamInsertFailureDbContext(options, insertConcurrentTeam: false);

        var create = () => CreateController(db).CreateTeam(new CreateTeamRequest { Name = "Biology", Slug = "biology" });

        await create.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetUsers_returns_only_admins_in_name_order_including_inactive_admins()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.AddRange(
            new User { IamId = "1", Name = "Zoe", IsAdmin = true },
            new User { IamId = "2", Name = "Amy", IsAdmin = true, IsActive = false },
            new User { IamId = "3", Name = "Other", IsAdmin = false });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).GetUsers();

        var users = ReadValue<List<AdminUserResponse>>(result);
        users.Select(user => user.Name).Should().Equal("Amy", "Zoe");
        users[0].IsActive.Should().BeFalse();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("directory@example.test")]
    [InlineData("0000000001")]
    [InlineData("person1")]
    [InlineData("  person1  ")]
    public async Task SearchPeople_finds_exact_email_iam_or_kerb_without_tracking(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).SearchPeople(query);

        var person = ReadValue<List<AdminPersonResponse>>(result).Should().ContainSingle().Which;
        person.IamId.Should().Be("0000000001");
        person.Name.Should().Be("Directory Person");
        person.Email.Should().Be("directory@example.test");
        person.Kerberos.Should().Be("person1");
        person.IsAdmin.Should().BeFalse();
        person.IsActive.Should().BeTrue();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "person1", "directory@example.test")]
    [InlineData("", "person1", "directory@example.test")]
    [InlineData("   ", "person1", "directory@example.test")]
    [InlineData("0000000001", null, "directory@example.test")]
    [InlineData("0000000001", "", "directory@example.test")]
    [InlineData("0000000001", "   ", "directory@example.test")]
    [InlineData("0000000001", "person1", null)]
    [InlineData("0000000001", "person1", "")]
    [InlineData("0000000001", "person1", "   ")]
    public async Task SearchPeople_excludes_people_missing_required_details(string? iamId, string? kerberos, string? email)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(new DirectoryPerson
        {
            IamId = iamId!, Name = "Incomplete Person", Kerberos = kerberos, Email = email,
        });

        var query = string.IsNullOrWhiteSpace(email) ? "person1" : "directory@example.test";
        var result = await CreateController(db).SearchPeople(query);

        ReadValue<List<AdminPersonResponse>>(result).Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("0000000001")]
    [InlineData("existing@example.test")]
    [InlineData("existingkerb")]
    public async Task SearchPeople_returns_existing_users_before_calling_the_directory(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing Admin", Email = "existing@example.test",
            Kerberos = "existingkerb", IsAdmin = true, IsActive = false,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rosetta = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable.") };

        var result = await CreateController(db, rosetta: rosetta).SearchPeople(query);

        var person = ReadValue<List<AdminPersonResponse>>(result).Should().ContainSingle().Which;
        person.Name.Should().Be("Existing Admin");
        person.Email.Should().Be("existing@example.test");
        person.IsAdmin.Should().BeTrue();
        person.IsActive.Should().BeFalse();
        person.Kerberos.Should().Be("existingkerb");
        rosetta.LookupCalls.Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("person")]
    [InlineData("%")]
    [InlineData("missing@example.test")]
    public async Task SearchPeople_does_not_expand_to_partial_or_wildcard_matches(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();

        var result = await CreateController(db).SearchPeople(query);

        ReadValue<List<AdminPersonResponse>>(result).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchPeople_rejects_empty_queries(string? query)
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).SearchPeople(query);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchPeople_rejects_oversized_queries()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).SearchPeople(new string('a', 129));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchPeople_bounds_matches_and_includes_existing_privilege_status()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        for (var index = 1; index <= 12; index++)
        {
            _people.Add(new DirectoryPerson
            {
                IamId = $"{index:D10}",
                Name = $"Person {index:D2}",
                Email = "shared@example.test",
                Kerberos = $"person{index}",
            });
        }
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing Admin", IsAdmin = true, IsActive = false,
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db).SearchPeople("shared@example.test");

        var people = ReadValue<List<AdminPersonResponse>>(result);
        people.Should().HaveCount(10);
        people[0].IsAdmin.Should().BeTrue();
        people[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SearchPeople_returns_the_resolved_directory_profile()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(new DirectoryPerson
        {
            IamId = "123", Name = "First Last",
            Email = "directory@example.test", Kerberos = "kerb",
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db).SearchPeople("directory@example.test");

        var person = ReadValue<List<AdminPersonResponse>>(result).Single();
        person.IamId.Should().Be("123");
        person.Kerberos.Should().Be("kerb");
        person.Name.Should().Be("First Last");
    }

    [Fact]
    public async Task AddUser_creates_an_active_admin_from_directory_data_before_first_login()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();
        var started = DateTimeOffset.UtcNow;

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        var response = ReadValue<AdminUserResponse>(result);
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        response.Id.Should().Be(user.Id);
        response.IamId.Should().Be("0000000001");
        user.Name.Should().Be("Directory Person");
        user.Email.Should().Be("directory@example.test");
        user.Kerberos.Should().Be("person1");
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        user.CreatedAt.Should().BeOnOrAfter(started);
        user.UpdatedAt.Should().BeOnOrAfter(user.CreatedAt);
        user.LastLoginAt.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "existing@example.test")]
    [InlineData("existingkerb", "existing@example.test")]
    [InlineData("existingkerb", null)]
    [InlineData("existingkerb", "")]
    [InlineData("   ", "   ")]
    public async Task AddUser_populates_missing_required_details_without_replacing_existing_profile_or_login_fields(
        string? existingKerberos, string? existingEmail)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing Name", Email = existingEmail,
            Kerberos = existingKerberos,
            CreatedAt = originalTime, UpdatedAt = originalTime, LastLoginAt = originalTime,
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        ReadValue<AdminUserResponse>(result).Name.Should().Be("Existing Name");
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.IsAdmin.Should().BeTrue();
        user.Name.Should().Be("Existing Name");
        user.Email.Should().Be(string.IsNullOrWhiteSpace(existingEmail) ? "directory@example.test" : existingEmail);
        user.Kerberos.Should().Be(string.IsNullOrWhiteSpace(existingKerberos) ? "person1" : existingKerberos);
        user.CreatedAt.Should().Be(originalTime);
        user.LastLoginAt.Should().Be(originalTime);
        user.UpdatedAt.Should().BeAfter(originalTime);
    }

    [Fact]
    public async Task AddUser_is_idempotent_for_an_existing_admin()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing Admin", IsAdmin = true, UpdatedAt = originalTime,
        });
        await db.SaveChangesAsync();
        var rosetta = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable.") };

        var result = await CreateController(db, rosetta: rosetta).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        ReadValue<AdminUserResponse>(result).Name.Should().Be("Existing Admin");
        (await db.Users.SingleAsync()).UpdatedAt.Should().Be(originalTime);
        rosetta.LookupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AddUser_does_not_reactivate_an_inactive_user()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        db.Users.Add(new User { IamId = "0000000001", Name = "Inactive User", IsActive = false });
        await db.SaveChangesAsync();
        var rosetta = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable.") };

        var result = await CreateController(db, rosetta: rosetta).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        var user = await db.Users.SingleAsync();
        user.IsActive.Should().BeFalse();
        user.IsAdmin.Should().BeFalse();
        rosetta.LookupCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "person1", "directory@example.test")]
    [InlineData("", "person1", "directory@example.test")]
    [InlineData("   ", "person1", "directory@example.test")]
    [InlineData("0000000001", null, "directory@example.test")]
    [InlineData("0000000001", "", "directory@example.test")]
    [InlineData("0000000001", "   ", "directory@example.test")]
    [InlineData("0000000001", "person1", null)]
    [InlineData("0000000001", "person1", "")]
    [InlineData("0000000001", "person1", "   ")]
    public async Task AddUser_rejects_incomplete_directory_details_without_creating_or_promoting_a_user(
        string? iamId, string? kerberos, string? email)
    {
        _people.Add(new DirectoryPerson
        {
            IamId = iamId!, Name = "Incomplete Person", Kerberos = kerberos, Email = email,
        });
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        foreach (var hasExistingUser in new[] { false, true })
        {
            using var db = TestDbContextFactory.CreateInMemory();
            if (hasExistingUser)
            {
                db.Users.Add(new User
                {
                    IamId = "0000000001", Name = "Active Booking User", IsActive = true, UpdatedAt = originalTime,
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var rosetta = new FakeRosettaService(db);
            var result = await CreateController(db, rosetta: rosetta).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

            result.Result.Should().BeOfType<NotFoundObjectResult>();
            rosetta.LookupCalls.Should().Equal("0000000001");
            db.ChangeTracker.HasChanges().Should().BeFalse();
            if (hasExistingUser)
            {
                var user = await db.Users.SingleAsync();
                user.IsAdmin.Should().BeFalse();
                user.IsActive.Should().BeTrue();
                user.UpdatedAt.Should().Be(originalTime);
            }
            else
            {
                (await db.Users.AnyAsync()).Should().BeFalse();
            }
        }
    }

    [Fact]
    public async Task AddUser_rechecks_required_directory_details_after_search()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = CreatePerson();
        _people.Add(person);
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var searchResult = await controller.SearchPeople("person1");
        var match = ReadValue<List<AdminPersonResponse>>(searchResult).Should().ContainSingle().Which;
        person.Email = null;

        var result = await controller.AddUser(new AddAdminUserRequest { IamId = match.IamId });

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "existingkerb")]
    [InlineData("   ", "existingkerb")]
    [InlineData("existing@example.test", null)]
    [InlineData("existing@example.test", "   ")]
    public async Task SearchPeople_uses_complete_directory_details_when_the_saved_user_is_incomplete(string? email, string? kerberos)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        _people.Add(CreatePerson());
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing User", Email = email, Kerberos = kerberos,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateController(db).SearchPeople("0000000001");

        var match = ReadValue<List<AdminPersonResponse>>(result).Should().ContainSingle().Which;
        match.IamId.Should().Be("0000000001");
        match.Name.Should().Be("Directory Person");
        match.Email.Should().Be("directory@example.test");
        match.Kerberos.Should().Be("person1");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task AddUser_does_not_create_a_user_when_the_directory_request_fails()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var rosetta = new FakeRosettaService(db) { Failure = new HttpRequestException("Directory unavailable.") };
        var add = () => CreateController(db, rosetta: rosetta)
            .AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        await add.Should().ThrowAsync<HttpRequestException>();

        db.Users.Should().BeEmpty();
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Fact]
    public async Task AddUser_rejects_a_person_no_longer_in_the_directory()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("12345678901")]
    public async Task AddUser_rejects_invalid_iam_ids(string iamId)
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = iamId });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, null, "login@example.test")]
    [InlineData(true, "existingkerb", "login@example.test")]
    [InlineData(true, "existingkerb", null)]
    [InlineData(true, "   ", "   ")]
    [InlineData(false, null, null)]
    public async Task AddUser_handles_a_concurrent_insert_without_overwriting_or_reactivating_the_user(
        bool isActive, string? existingKerberos, string? existingEmail)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AdminRace_{Guid.NewGuid():N}").Options;
        using var db = new ConcurrentInsertDbContext(options, isActive, existingKerberos, existingEmail);
        _people.Add(CreatePerson());
        await db.SaveChangesAsync();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Concurrent Login");
        user.Email.Should().Be(isActive && string.IsNullOrWhiteSpace(existingEmail) ? "directory@example.test" : existingEmail);
        user.Kerberos.Should().Be(isActive && string.IsNullOrWhiteSpace(existingKerberos) ? "person1" : existingKerberos);
        user.LastLoginAt.Should().NotBeNull();
        user.IsActive.Should().Be(isActive);
        user.IsAdmin.Should().Be(isActive);
        if (isActive)
        {
            ReadValue<AdminUserResponse>(result).Id.Should().Be(user.Id);
        }
        else
        {
            result.Result.Should().BeOfType<ConflictObjectResult>();
        }
    }

    [Fact]
    public async Task RemoveUser_revokes_only_the_flag_and_preserves_the_user_record()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        var user = new User
        {
            IamId = "0000000001", Name = "Other Admin", Email = "other@example.test",
            IsAdmin = true, IsActive = false, CreatedAt = originalTime, UpdatedAt = originalTime,
            LastLoginAt = originalTime,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await CreateController(db).RemoveUser(user.Id);

        result.Should().BeOfType<NoContentResult>();
        db.ChangeTracker.Clear();
        var saved = await db.Users.SingleAsync();
        saved.IsAdmin.Should().BeFalse();
        saved.IsActive.Should().BeFalse();
        saved.Name.Should().Be("Other Admin");
        saved.Email.Should().Be("other@example.test");
        saved.CreatedAt.Should().Be(originalTime);
        saved.LastLoginAt.Should().Be(originalTime);
        saved.UpdatedAt.Should().BeAfter(originalTime);
    }

    [Theory]
    [InlineData("current-admin")]
    [InlineData(" CURRENT-ADMIN ")]
    public async Task RemoveUser_rejects_self_removal_using_iam_instead_of_name_identifier(string iamId)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var user = new User { IamId = iamId, Name = "Current Admin", IsAdmin = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await CreateController(db).RemoveUser(user.Id);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Users.SingleAsync()).IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveUser_rejects_missing_iam_and_unknown_users()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        (await CreateController(db, iamId: null).RemoveUser(1)).Should().BeOfType<ForbidResult>();
        (await CreateController(db).RemoveUser(1)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Every_admin_endpoint_requires_authorization_and_mutations_validate_antiforgery()
    {
        var controller = typeof(AdminController);
        controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Should().Contain(attribute => attribute.Policy == AuthenticationHelper.SiteAdminPolicy);
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>(inherit: true).Should().NotBeNull();
        controller.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        foreach (var name in new[]
        {
            nameof(AdminController.Access),
            nameof(AdminController.GetUsers), nameof(AdminController.SearchPeople),
            nameof(AdminController.AddUser), nameof(AdminController.RemoveUser),
            nameof(AdminController.GetTeams), nameof(AdminController.CreateTeam),
        })
        {
            controller.GetMethod(name)!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
            controller.GetMethod(name)!.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        }
        controller.GetMethod(nameof(AdminController.AddUser))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("users");
        controller.GetMethod(nameof(AdminController.RemoveUser))!
            .GetCustomAttribute<HttpDeleteAttribute>()!.Template.Should().Be("users/{id:int}");
        controller.GetMethod(nameof(AdminController.CreateTeam))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("teams");
    }

    private static T ReadValue<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static DirectoryPerson CreatePerson() => new()
    {
        IamId = "0000000001", Name = "Directory Person",
        Email = "directory@example.test", Kerberos = "person1",
    };

    private AdminController CreateController(AppDbContext db, string? iamId = "current-admin", FakeRosettaService? rosetta = null)
    {
        rosetta ??= new FakeRosettaService(db);
        rosetta.People.AddRange(_people);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "different-identity-id") };
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new AdminController(db, rosetta)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private sealed class TeamInsertFailureDbContext : AppDbContext
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly bool _insertConcurrentTeam;

        public TeamInsertFailureDbContext(DbContextOptions<AppDbContext> options, bool insertConcurrentTeam)
            : base(options)
        {
            _options = options;
            _insertConcurrentTeam = insertConcurrentTeam;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var candidate = ChangeTracker.Entries<Team>().Single(entry => entry.State == EntityState.Added);
            if (_insertConcurrentTeam)
            {
                using var concurrentDb = new AppDbContext(_options);
                concurrentDb.Teams.Add(new Team { Name = "Concurrent Team", Slug = candidate.Entity.Slug });
                await concurrentDb.SaveChangesAsync(cancellationToken);
            }

            throw new DbUpdateException("Simulated team persistence failure.");
        }
    }

    private sealed class ConcurrentInsertDbContext(DbContextOptions<AppDbContext> options, bool isActive, string? kerberos, string? email)
        : AppDbContext(options)
    {
        private bool _insertedConcurrentUser;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var candidate = ChangeTracker.Entries<User>().FirstOrDefault(entry => entry.State == EntityState.Added);
            if (!_insertedConcurrentUser && candidate != null)
            {
                _insertedConcurrentUser = true;
                using var concurrentDb = new AppDbContext(options);
                concurrentDb.Users.Add(new User
                {
                    IamId = candidate.Entity.IamId, Name = "Concurrent Login", Email = email,
                    Kerberos = kerberos,
                    IsActive = isActive, LastLoginAt = DateTimeOffset.UtcNow,
                });
                await concurrentDb.SaveChangesAsync(cancellationToken);
                throw new DbUpdateException("Simulated concurrent insert of the unique IAM ID.");
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}

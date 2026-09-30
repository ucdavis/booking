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

namespace Server.Tests.Controllers;

public class AdminControllerTests
{
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
        db.People.Add(CreatePerson());
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
    [InlineData("person")]
    [InlineData("%")]
    [InlineData("missing@example.test")]
    public async Task SearchPeople_does_not_expand_to_partial_or_wildcard_matches(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(CreatePerson());
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
            db.People.Add(new Person
            {
                IamId = $"{index:D10}",
                FullName = $"Person {index:D2}",
                Email = "shared@example.test",
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
    public async Task SearchPeople_trims_fixed_width_output_and_uses_available_name_parts()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(new Person
        {
            IamId = "123       ", FirstName = "First", LastName = "Last",
            Email = "directory@example.test", UserId = "kerb    ",
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
        db.People.Add(CreatePerson());
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
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        user.CreatedAt.Should().BeOnOrAfter(started);
        user.UpdatedAt.Should().BeOnOrAfter(user.CreatedAt);
        user.LastLoginAt.Should().BeNull();
    }

    [Fact]
    public async Task AddUser_promotes_existing_user_without_replacing_profile_or_login_fields()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(CreatePerson());
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing Name", Email = "existing@example.test",
            CreatedAt = originalTime, UpdatedAt = originalTime, LastLoginAt = originalTime,
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        ReadValue<AdminUserResponse>(result).Name.Should().Be("Existing Name");
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.IsAdmin.Should().BeTrue();
        user.Name.Should().Be("Existing Name");
        user.Email.Should().Be("existing@example.test");
        user.CreatedAt.Should().Be(originalTime);
        user.LastLoginAt.Should().Be(originalTime);
        user.UpdatedAt.Should().BeAfter(originalTime);
    }

    [Fact]
    public async Task AddUser_is_idempotent_for_an_existing_admin()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(CreatePerson());
        var originalTime = DateTimeOffset.UtcNow.AddDays(-1);
        db.Users.Add(new User
        {
            IamId = "0000000001", Name = "Existing Admin", IsAdmin = true, UpdatedAt = originalTime,
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        ReadValue<AdminUserResponse>(result).Name.Should().Be("Existing Admin");
        (await db.Users.SingleAsync()).UpdatedAt.Should().Be(originalTime);
    }

    [Fact]
    public async Task AddUser_does_not_reactivate_an_inactive_user()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(CreatePerson());
        db.Users.Add(new User { IamId = "0000000001", Name = "Inactive User", IsActive = false });
        await db.SaveChangesAsync();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        result.Result.Should().BeOfType<ConflictObjectResult>();
        var user = await db.Users.SingleAsync();
        user.IsActive.Should().BeFalse();
        user.IsAdmin.Should().BeFalse();
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
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddUser_handles_a_concurrent_insert_without_overwriting_or_reactivating_the_user(bool isActive)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AdminRace_{Guid.NewGuid():N}").Options;
        using var db = new ConcurrentInsertDbContext(options, isActive);
        db.People.Add(CreatePerson());
        await db.SaveChangesAsync();

        var result = await CreateController(db).AddUser(new AddAdminUserRequest { IamId = "0000000001" });

        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Concurrent Login");
        user.Email.Should().Be("login@example.test");
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
        foreach (var name in new[]
        {
            nameof(AdminController.Access), nameof(AdminController.AntiforgeryToken),
            nameof(AdminController.GetUsers), nameof(AdminController.SearchPeople),
            nameof(AdminController.AddUser), nameof(AdminController.RemoveUser),
        })
        {
            controller.GetMethod(name)!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        }
        controller.GetMethod(nameof(AdminController.AddUser))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        controller.GetMethod(nameof(AdminController.RemoveUser))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
    }

    private static T ReadValue<T>(ActionResult<T> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<T>().Subject;

    private static Person CreatePerson() => new()
    {
        IamId = "0000000001", FullName = "Directory Person",
        Email = "directory@example.test", UserId = "person1",
    };

    private static AdminController CreateController(AppDbContext db, string? iamId = "current-admin")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "different-identity-id") };
        if (iamId != null)
        {
            claims.Add(new Claim("ucdPersonIAMID", iamId));
        }
        return new AdminController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private sealed class ConcurrentInsertDbContext(DbContextOptions<AppDbContext> options, bool isActive)
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
                    IamId = candidate.Entity.IamId, Name = "Concurrent Login", Email = "login@example.test",
                    IsActive = isActive, LastLoginAt = DateTimeOffset.UtcNow,
                });
                await concurrentDb.SaveChangesAsync(cancellationToken);
                throw new DbUpdateException("Simulated concurrent insert of the unique IAM ID.");
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}

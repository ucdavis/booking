using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Data;

public class DbInitializerTests
{
    private const string AdminIamId = "FAKE000055";

    [Fact]
    public async Task Fresh_development_seed_creates_person_55_as_admin_only_once()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        await SeedDevelopmentAsync(db);

        (await db.People.AnyAsync(person => person.IamId == AdminIamId)).Should().BeTrue();
        var seeded = await db.Users.AsNoTracking().SingleAsync();
        seeded.IamId.Should().Be(AdminIamId);
        seeded.Name.Should().Be("Fake055 User055");
        seeded.Email.Should().Be("fake.user055@example.invalid");
        seeded.IsAdmin.Should().BeTrue();
        seeded.IsActive.Should().BeTrue();
        seeded.CreatedAt.Should().NotBe(default);
        seeded.UpdatedAt.Should().Be(seeded.CreatedAt);
        seeded.LastLoginAt.Should().BeNull();
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var repeated = await db.Users.AsNoTracking().SingleAsync();
        repeated.Should().BeEquivalentTo(seeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_person_does_not_create_or_promote_a_matching_user(bool hasExistingUser)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(new Person { IamId = "OTHER00001", FullName = "Unrelated Person", IsActiveInIam = true });
        var originalTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var existing = new User
        {
            IamId = AdminIamId,
            Name = "Existing User",
            Email = "existing@example.test",
            IsAdmin = false,
            IsActive = false,
            CreatedAt = originalTime,
            UpdatedAt = originalTime.AddDays(1),
            LastLoginAt = originalTime.AddHours(1),
        };
        if (hasExistingUser)
        {
            db.Users.Add(existing);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        (await db.People.AnyAsync(person => person.IamId == AdminIamId)).Should().BeFalse();
        if (hasExistingUser)
        {
            var saved = await db.Users.AsNoTracking().SingleAsync();
            saved.Should().BeEquivalentTo(existing);
        }
        else
        {
            (await db.Users.AnyAsync()).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_user_is_promoted_without_overwriting_profile_or_account_state(bool isActive)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.People.Add(new Person
        {
            IamId = AdminIamId,
            FullName = "Directory Name",
            Email = "directory@example.test",
            IsActiveInIam = true,
        });
        var originalTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var existing = new User
        {
            IamId = AdminIamId,
            Name = "Existing Name",
            Email = "existing@example.test",
            IsAdmin = false,
            IsActive = isActive,
            CreatedAt = originalTime,
            UpdatedAt = originalTime.AddDays(1),
            LastLoginAt = originalTime.AddHours(1),
        };
        db.Users.Add(existing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var promoted = await db.Users.AsNoTracking().SingleAsync();
        promoted.Id.Should().Be(existing.Id);
        promoted.IamId.Should().Be(existing.IamId);
        promoted.IsAdmin.Should().BeTrue();
        promoted.Name.Should().Be(existing.Name);
        promoted.Email.Should().Be(existing.Email);
        promoted.IsActive.Should().Be(existing.IsActive);
        promoted.CreatedAt.Should().Be(existing.CreatedAt);
        promoted.LastLoginAt.Should().Be(existing.LastLoginAt);
        promoted.UpdatedAt.Should().BeAfter(existing.UpdatedAt);
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var repeated = await db.Users.AsNoTracking().SingleAsync();
        repeated.Should().BeEquivalentTo(promoted);
    }

    private static Task SeedDevelopmentAsync(AppDbContext db)
    {
        // Exercise only seeding with the in-memory provider; InitializeAsync applies migrations.
        var initializer = new DbInitializer(db, NullLogger<DbInitializer>.Instance);
        var seed = typeof(DbInitializer).GetMethod("SeedDevelopmentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)seed.Invoke(initializer, new object[] { CancellationToken.None })!;
    }
}

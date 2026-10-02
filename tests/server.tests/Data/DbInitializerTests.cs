using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Data;

public class DbInitializerTests
{
    [Fact]
    public async Task Fresh_development_seed_creates_weather_once_without_creating_users()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        await SeedDevelopmentAsync(db);

        var seeded = await db.WeatherForecasts.AsNoTracking().OrderBy(forecast => forecast.Date).ToListAsync();
        seeded.Should().HaveCount(10);
        seeded.First().Date.Should().Be(new DateOnly(2025, 1, 1));
        seeded.Last().Date.Should().Be(new DateOnly(2025, 1, 10));
        (await db.Users.AnyAsync()).Should().BeFalse();
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var repeated = await db.WeatherForecasts.AsNoTracking().OrderBy(forecast => forecast.Date).ToListAsync();
        repeated.Should().BeEquivalentTo(seeded);
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Existing_weather_is_preserved()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var existing = new WeatherForecast
        {
            Date = new DateOnly(2025, 2, 1),
            TemperatureC = 21,
            Summary = "Existing forecast",
        };
        db.WeatherForecasts.Add(existing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var saved = await db.WeatherForecasts.AsNoTracking().SingleAsync();
        saved.Should().BeEquivalentTo(existing);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Development_seed_preserves_existing_user_profile_and_permissions(bool isActive, bool isAdmin)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var originalTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var existing = new User
        {
            IamId = "FAKE000055",
            Name = "Existing Name",
            Email = "existing@example.test",
            Kerberos = "existingkerb",
            IsAdmin = isAdmin,
            IsActive = isActive,
            CreatedAt = originalTime,
            UpdatedAt = originalTime.AddDays(1),
            LastLoginAt = originalTime.AddHours(1),
        };
        db.Users.Add(existing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var saved = await db.Users.AsNoTracking().SingleAsync();
        saved.Should().BeEquivalentTo(existing);
        db.ChangeTracker.Clear();

        await SeedDevelopmentAsync(db);

        var repeated = await db.Users.AsNoTracking().SingleAsync();
        repeated.Should().BeEquivalentTo(saved);
    }

    private static Task SeedDevelopmentAsync(AppDbContext db)
    {
        // Exercise only seeding with the in-memory provider; InitializeAsync applies migrations.
        var initializer = new DbInitializer(db, NullLogger<DbInitializer>.Instance);
        var seed = typeof(DbInitializer).GetMethod("SeedDevelopmentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)seed.Invoke(initializer, new object[] { CancellationToken.None })!;
    }
}

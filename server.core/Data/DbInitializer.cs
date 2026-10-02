using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Core.Data;
using Server.Core.Domain;

public interface IDbInitializer
{
    Task InitializeAsync(bool includeSampleData, CancellationToken cancellationToken = default);
}

public class DbInitializer : IDbInitializer
{
    private readonly AppDbContext _db;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext db, ILogger<DbInitializer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync(bool includeSampleData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying database migrations...");
        await _db.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Migrations applied.");

        if (includeSampleData)
        {
            await SeedDevelopmentAsync(cancellationToken);
        }
        else
        {
            await SeedProductionSafeAsync(cancellationToken);
        }
    }

    private async Task SeedDevelopmentAsync(CancellationToken ct)
    {
        if (!await _db.WeatherForecasts.AnyAsync(ct))
        {
            // Fixed dates make fresh sandboxes reproducible for screenshots and investigation.
            var firstDate = new DateOnly(2025, 1, 1);
            var forecasts = new[]
            {
                new WeatherForecast { Date = firstDate.AddDays(0), TemperatureC = 18, Summary = "Cool" },
                new WeatherForecast { Date = firstDate.AddDays(1), TemperatureC = 22, Summary = "Mild" },
                new WeatherForecast { Date = firstDate.AddDays(2), TemperatureC = 35, Summary = "Hot" },
                new WeatherForecast { Date = firstDate.AddDays(3), TemperatureC = 15, Summary = "Chilly" },
                new WeatherForecast { Date = firstDate.AddDays(4), TemperatureC = 8, Summary = "Freezing" },
                new WeatherForecast { Date = firstDate.AddDays(5), TemperatureC = 25, Summary = "Warm" },
                new WeatherForecast { Date = firstDate.AddDays(6), TemperatureC = 28, Summary = "Balmy" },
                new WeatherForecast { Date = firstDate.AddDays(7), TemperatureC = 12, Summary = "Cold" },
                new WeatherForecast { Date = firstDate.AddDays(8), TemperatureC = 32, Summary = "Scorching" },
                new WeatherForecast { Date = firstDate.AddDays(9), TemperatureC = 20, Summary = "Pleasant" }
            };

            _db.WeatherForecasts.AddRange(forecasts);
            await _db.SaveChangesAsync(ct);
        }

        if (!await _db.People.AnyAsync(ct))
        {
            var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 1; i <= 100; i++)
            {
                _db.People.Add(new Person
                {
                    IamId = $"FAKE{i:D6}",
                    EmployeeId = $"FAKE{i:D4}",
                    FirstName = $"Fake{i:D3}",
                    LastName = $"User{i:D3}",
                    FullName = $"Fake{i:D3} User{i:D3}",
                    IsActiveInIam = i != 10,
                    IsEmployee = true,
                    IsHsEmployee = false,
                    IsFaculty = false,
                    IsStudent = false,
                    IsStaff = true,
                    IsExternal = false,
                    IsCampusEmployee = "Y",
                    UserId = $"fake{i:D4}",
                    Email = $"fake.user{i:D3}@example.invalid",
                    ModifyDate = seedDate,
                    ModifyDateRaw = "2025-01-01 00:00:00",
                    FirstIngestedAt = seedDate,
                    LastFetchedAt = seedDate,
                    SourceEndpoint = "development-seed",
                    PromotedAt = seedDate
                });
            }

            await _db.SaveChangesAsync(ct);
        }

        const string adminIamId = "FAKE000055";
        var person = await _db.People.AsNoTracking()
            .SingleOrDefaultAsync(person => person.IamId == adminIamId, ct);
        if (person == null)
        {
            return;
        }

        var user = await _db.Users.SingleOrDefaultAsync(user => user.IamId == adminIamId, ct);
        if (user?.IsAdmin == true)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (user == null)
        {
            var name = string.IsNullOrWhiteSpace(person.FullName)
                ? $"{person.FirstName} {person.LastName}".Trim()
                : person.FullName.Trim();
            user = new User
            {
                IamId = adminIamId,
                Name = string.IsNullOrWhiteSpace(name) ? adminIamId : name,
                Email = person.Email?.Trim(),
                CreatedAt = now,
            };
            _db.Users.Add(user);
        }

        user.IsAdmin = true;
        user.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    // just a placeholder for any production-safe seeding
    private Task SeedProductionSafeAsync(CancellationToken ct)
        => Task.CompletedTask;
}

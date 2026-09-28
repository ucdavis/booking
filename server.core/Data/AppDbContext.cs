using Microsoft.EntityFrameworkCore;
using Server.Core.Domain;
using Server.Core.Domain.Administration;
using Server.Core.Domain.Bookings;
using Server.Core.Domain.CalendarPublication;
using Server.Core.Domain.ResourceSetup;

namespace Server.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    public DbSet<WeatherForecast> WeatherForecasts => Set<WeatherForecast>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamPermission> TeamPermissions => Set<TeamPermission>();

    public DbSet<Space> Spaces => Set<Space>();

    public DbSet<TeamSpace> TeamSpaces => Set<TeamSpace>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<ResourceTemplate> ResourceTemplates => Set<ResourceTemplate>();

    public DbSet<ResourceConfig> ResourceConfigs => Set<ResourceConfig>();

    public DbSet<CatalogFile> Files => Set<CatalogFile>();

    public DbSet<ReservationSeries> ReservationSeries => Set<ReservationSeries>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ReservationEvent> ReservationEvents => Set<ReservationEvent>();

    public DbSet<ReservationNotification> Notifications => Set<ReservationNotification>();

    public DbSet<ScheduleException> ScheduleExceptions => Set<ScheduleException>();

    public DbSet<CalendarFeed> CalendarFeeds => Set<CalendarFeed>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Fabric owns this lookup; Grove can query it without managing its schema.
        modelBuilder.Entity<Person>()
            .ToTable("People", table => table.ExcludeFromMigrations());

        modelBuilder.Entity<Person>()
            .HasKey(person => person.IamId)
            .HasName("PK_People")
            .IsClustered();
    }
}

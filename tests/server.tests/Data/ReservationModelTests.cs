using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Core.Domain.Administration;
using Server.Core.Domain.Bookings;
using Server.Core.Domain.ResourceSetup;

namespace Server.Tests.Data;

public class ReservationModelTests
{
    [Fact]
    public void SqlServer_model_generates_all_reservation_tables_without_connecting_to_a_database()
    {
        using var context = CreateContext();

        // GenerateCreateScript validates provider mappings but neither opens a connection
        // nor creates a migration, updates the snapshot, or executes the returned SQL.
        var script = context.Database.GenerateCreateScript();

        string[] tables =
        [
            "Users", "Teams", "TeamPermissions", "Spaces", "TeamSpaces", "Resources",
            "ResourceTemplates", "ResourceConfigs", "Files", "ReservationSeries",
            "Reservations", "ReservationEvents", "Notifications", "ScheduleExceptions", "CalendarFeeds"
        ];

        foreach (var table in tables)
        {
            script.Should().Contain($"CREATE TABLE [{table}]");
        }

        script.Should().Contain("[IamId] varchar(50) NOT NULL");
        script.Should().Contain("[Amount] decimal(12,2) NULL");
        script.Should().Contain("[LocalDate] date NOT NULL");
        script.Should().Contain("[StartsAt] datetimeoffset NOT NULL");
        script.Should().NotContain("ON DELETE CASCADE");
        script.Should().NotContain("CREATE TABLE [People]", "Fabric owns the People table");
    }

    [Fact]
    public void Composite_foreign_keys_preserve_resource_and_request_ownership()
    {
        using var context = CreateContext();
        var script = context.Database.GenerateCreateScript();

        script.Should().Contain(
            "FOREIGN KEY ([ParentResourceId], [SpaceId], [TeamId]) REFERENCES [Resources] ([Id], [SpaceId], [TeamId])");
        script.Should().Contain(
            "FOREIGN KEY ([ResourceConfigId], [ResourceId]) REFERENCES [ResourceConfigs] ([Id], [ResourceId])");
        script.Should().Contain(
            "FOREIGN KEY ([ReservationId], [ReservationSeriesId]) REFERENCES [Reservations] ([Id], [ReservationSeriesId])");

        var model = context.GetService<IDesignTimeModel>().Model;
        var resource = model.FindEntityType(typeof(Resource))!;
        resource.FindProperty(nameof(Resource.ParentResourceId))!.IsNullable.Should().BeTrue();
        resource.FindProperty(nameof(Resource.SpaceId))!.IsNullable.Should().BeFalse();
        resource.FindProperty(nameof(Resource.TeamId))!.IsNullable.Should().BeFalse();

        var notification = model.FindEntityType(typeof(ReservationNotification))!;
        notification.FindProperty(nameof(ReservationNotification.ReservationId))!.IsNullable.Should().BeTrue();
        notification.FindProperty(nameof(ReservationNotification.ReservationSeriesId))!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Root_resource_names_remain_in_the_unique_sibling_index()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var index = FindIndex<Resource>(model, "SpaceId", "ParentResourceId", "NameKey");

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().BeNull("root resources must participate even though ParentResourceId is null");

        var script = context.Database.GenerateCreateScript();
        script.Should().Contain("ON [Resources] ([SpaceId], [ParentResourceId], [NameKey]);");
    }

    [Fact]
    public void Optional_tokens_and_external_invoice_ids_allow_multiple_unassigned_rows()
    {
        using var context = CreateContext();
        var script = context.Database.GenerateCreateScript();

        script.Should().Contain("ON [ReservationSeries] ([ShareToken]) WHERE [ShareToken] IS NOT NULL;");
        script.Should().Contain("ON [Reservations] ([ShareToken]) WHERE [ShareToken] IS NOT NULL;");
        script.Should().Contain("ON [Reservations] ([PaymentsInvoiceId]) WHERE [PaymentsInvoiceId] IS NOT NULL;");

        var model = context.GetService<IDesignTimeModel>().Model;
        FindIndex<ReservationSeries>(model, "ShareToken").IsUnique.Should().BeTrue();
        FindIndex<Reservation>(model, "ShareToken").IsUnique.Should().BeTrue();
        FindIndex<Reservation>(model, "PaymentsInvoiceId").IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Schedule_uniqueness_is_limited_to_the_selected_owner()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var teamIndex = FindIndex<ScheduleException>(model, "TeamId", "LocalDate");
        var resourceIndex = FindIndex<ScheduleException>(model, "ResourceId", "LocalDate");

        teamIndex.IsUnique.Should().BeTrue();
        teamIndex.GetFilter().Should().Be("[TeamId] IS NOT NULL");
        resourceIndex.IsUnique.Should().BeTrue();
        resourceIndex.GetFilter().Should().Be("[ResourceId] IS NOT NULL");

        var referenceIndex = FindIndex<Space>(model, "ReferenceNumber");
        referenceIndex.IsUnique.Should().BeTrue();
        referenceIndex.GetFilter().Should().Contain("[IsOfficialFacility] = 1");
    }

    [Fact]
    public void Foreign_keys_are_explicit_and_preserve_business_history()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        model.GetEntityTypes().SelectMany(entity => entity.GetProperties())
            .Should().NotContain(property => property.IsShadowProperty(),
                "an accidental second relationship would introduce an unrequested foreign-key column");
        model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys())
            .Should().OnlyContain(key => key.DeleteBehavior == DeleteBehavior.NoAction);

        var person = model.FindEntityType(typeof(Person))!;
        person.IsTableExcludedFromMigrations().Should().BeTrue();
        person.GetForeignKeys().Should().BeEmpty();
        person.GetReferencingForeignKeys().Should().BeEmpty("Fabric People is an external lookup");
    }

    [Fact]
    public void Required_true_defaults_can_still_be_explicitly_saved_as_false()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var trueDefaults = model.GetEntityTypes().SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(bool) && Equals(property.GetDefaultValue(), true))
            .ToList();

        trueDefaults.Should().NotBeEmpty();
        trueDefaults.Should().OnlyContain(property => Equals(property.Sentinel, true),
            "false must be sent to SQL Server instead of being replaced by its true default");

        var publicAccess = model.FindEntityType(typeof(Resource))!.FindProperty(nameof(Resource.PublicAccess))!;
        publicAccess.GetDefaultValueSql().Should().BeNull();
        publicAccess.GetDefaultValue().Should().BeNull("children store null and inherit their root's policy");
    }

    [Fact]
    public void Reservation_revision_participates_in_optimistic_concurrency()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(Reservation))!
            .FindProperty(nameof(Reservation.Revision))!;

        property.IsConcurrencyToken.Should().BeTrue();
        property.GetDefaultValue().Should().Be(1);
        property.ValueGenerated.Should().NotBe(ValueGenerated.OnAddOrUpdate,
            "business workflows increment Revision together with their event rather than using a rowversion");
    }

    private static IIndex FindIndex<TEntity>(IModel model, params string[] properties)
        => model.FindEntityType(typeof(TEntity))!.GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(properties));

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(local);Database=GroveModelTests;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }
}

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Services;

namespace Server.Tests.Services;

public class EmulationServiceTests
{
    [Fact]
    public async Task Search_returns_accounts_before_directory_only_people_without_duplicates_or_writes()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.AddRange(
            new User { IamId = "account", Name = "Stored Name", Email = "match@example.test" },
            new User { IamId = "no-person", Name = "Standalone Account", Email = "match@example.test" });
        db.People.AddRange(
            new Person { IamId = "account", FullName = "Directory Name", Email = "match@example.test", IsActiveInIam = true },
            new Person { IamId = "new-person", FullName = "New Person", Email = "match@example.test", IsActiveInIam = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var matches = await Service(db).SearchAsync("match@example.test", default);

        matches.Select(match => match.IamId).Should().Equal("no-person", "account", "new-person");
        matches.Single(match => match.IamId == "account").Name.Should().Be("Stored Name");
        matches.Single(match => match.IamId == "account").HasUserAccount.Should().BeTrue();
        matches.Single(match => match.IamId == "no-person").IsActiveInIam.Should().BeNull();
        matches.Last().HasUserAccount.Should().BeFalse();
        (await db.Users.CountAsync()).Should().Be(2);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("person1")]
    [InlineData("directory@example.test")]
    public async Task Search_uses_directory_identifiers_to_find_the_existing_account(string query)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "1000000001", Name = "Stored Name", Email = "old@example.test" });
        db.People.Add(Person());
        await db.SaveChangesAsync();

        var matches = await Service(db).SearchAsync(query, default);

        matches.Should().ContainSingle().Which.HasUserAccount.Should().BeTrue();
        matches[0].IamId.Should().Be("1000000001");
    }

    [Fact]
    public async Task Search_is_exact_and_limits_results_to_ten()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        for (var index = 0; index < 12; index++)
        {
            db.Users.Add(new User { IamId = $"account-{index}", Name = $"Name {index:D2}", Email = "match@example.test" });
        }
        await db.SaveChangesAsync();
        var service = Service(db);

        (await service.SearchAsync("match@example.test", default)).Should().HaveCount(10);
        (await service.SearchAsync("match", default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_from_people_does_not_record_login_grant_admin_or_add_memberships()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person();
        person.FullName = " Directory Person ";
        person.Email = " directory@example.test ";
        db.People.Add(person);
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = "true", ["DevelopmentData:AdminIamIds"] = person.IamId,
        }).Build();

        var (user, error) = await Service(db, configuration: configuration)
            .FindOrCreateTargetAsync(" 1000000001 ", default);

        error.Should().BeNull();
        user.Should().NotBeNull();
        user!.Name.Should().Be("Directory Person");
        user.Email.Should().Be("directory@example.test");
        user.IsAdmin.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();
        user.UpdatedAt.Should().Be(user.CreatedAt).And.NotBe(default);
        (await db.TeamPermissions.AnyAsync()).Should().BeFalse();
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Existing_accounts_without_people_are_reused_without_modifying_profile_or_timestamps()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var originalTime = DateTimeOffset.UtcNow.AddDays(-10);
        var user = new User
        {
            IamId = "sandbox-10001", Name = "Original", Email = "original@example.test", IsAdmin = true,
            CreatedAt = originalTime, UpdatedAt = originalTime, LastLoginAt = originalTime,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Service(db).FindOrCreateTargetAsync(user.IamId, default);

        result.User.Should().BeSameAs(user);
        result.Error.Should().BeNull();
        user.Name.Should().Be("Original");
        user.Email.Should().Be("original@example.test");
        user.IsAdmin.Should().BeTrue();
        user.UpdatedAt.Should().Be(originalTime);
        user.LastLoginAt.Should().Be(originalTime);
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Inactive_accounts_and_inactive_directory_people_are_rejected(bool activeUser, bool activePerson)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "1000000001", Name = "Existing", IsActive = activeUser });
        var person = Person();
        person.IsActiveInIam = activePerson;
        db.People.Add(person);
        await db.SaveChangesAsync();
        var service = Service(db);

        var result = await service.FindOrCreateTargetAsync(person.IamId, default);

        result.User.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
        (await service.GetActiveTargetAsync(person.IamId, default)).Should().BeNull();
    }

    [Fact]
    public async Task Missing_identity_and_inactive_directory_person_do_not_create_an_account()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person();
        person.IsActiveInIam = false;
        db.People.Add(person);
        await db.SaveChangesAsync();
        var service = Service(db);

        var missing = await service.FindOrCreateTargetAsync("missing", default);
        var inactive = await service.FindOrCreateTargetAsync(person.IamId, default);

        missing.User.Should().BeNull();
        missing.Error.Should().BeNull();
        inactive.User.Should().BeNull();
        inactive.Error.Should().NotBeNull();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_creation_reuses_the_winner_and_preserves_flags_and_login(bool winnerActive)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"EmulationRace_{Guid.NewGuid():N}").Options;
        using (var seed = new AppDbContext(options))
        {
            seed.People.Add(Person());
            await seed.SaveChangesAsync();
        }
        using var db = new ConcurrentInsertDbContext(options, winnerActive, insertWinner: true);

        var result = await Service(db).FindOrCreateTargetAsync("1000000001", default);

        (result.User != null).Should().Be(winnerActive);
        (result.Error != null).Should().Be(!winnerActive);
        var user = await db.Users.SingleAsync();
        user.Name.Should().Be("Concurrent Sign-in");
        user.IsAdmin.Should().BeTrue();
        user.IsActive.Should().Be(winnerActive);
        user.LastLoginAt.Should().NotBeNull();
        db.ChangeTracker.Entries<User>().Should().NotContain(entry => entry.State == EntityState.Added);
    }

    [Fact]
    public async Task Unrelated_database_failures_are_not_treated_as_duplicate_users()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"EmulationFailure_{Guid.NewGuid():N}").Options;
        using (var seed = new AppDbContext(options))
        {
            seed.People.Add(Person());
            await seed.SaveChangesAsync();
        }
        using var db = new ConcurrentInsertDbContext(options, winnerActive: true, insertWinner: false);

        var create = () => Service(db).FindOrCreateTargetAsync("1000000001", default);

        await create.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData("sandbox-10001", "sandbox-sample", true)]
    [InlineData("sandbox-10002", "sandbox-basic", false)]
    [InlineData("1000000001", "local-person:1000000001", false)]
    public async Task Local_effective_principals_preserve_persona_roles_without_borrowing_actor_claims(
        string iamId, string identifier, bool sampleRole)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var target = new User { IamId = iamId, Name = "Target", Email = "target@example.test", IsAdmin = true };

        var principal = await Service(db).CreatePrincipalAsync(target);

        principal.Identity!.Name.Should().Be("Target");
        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(identifier);
        principal.FindFirst("ucdPersonIAMID")!.Value.Should().Be(iamId);
        principal.IsInRole("User").Should().BeTrue();
        principal.IsInRole("SampleRole").Should().Be(sampleRole);
        principal.IsInRole("Admin").Should().BeFalse();
        principal.HasClaim(EmulationService.EmulatingClaimType, "true").Should().BeTrue();
    }

    [Fact]
    public async Task Entra_effective_principals_use_application_role_resolution()
    {
        using var db = TestDbContextFactory.CreateInMemory();

        var principal = await Service(db, local: false)
            .CreatePrincipalAsync(new User { Id = 14, IamId = "1000000001", Name = "Target" });

        principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("emulated-user:14");
        principal.IsInRole("User").Should().BeTrue();
        principal.IsInRole("SampleRole").Should().BeTrue();
        principal.HasClaim(EmulationService.EmulatingClaimType, "true").Should().BeTrue();
    }

    [Fact]
    public void Selection_cookie_is_session_only_secure_and_bound_to_actor_and_login_session()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var service = Service(db);
        var actor = Actor();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        service.Start(context, actor, "session-one", new User { Id = 7, IamId = "target", Name = "Target" });
        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());

        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        cookie.Path.Value.Should().Be("/");
        cookie.Expires.Should().BeNull();
        cookie.MaxAge.Should().BeNull();
        var request = WithCookie(cookie);
        service.HasSelection(request).Should().BeTrue();
        service.ReadTarget(request, actor, "session-one").Should().Be("target");
        service.ReadTarget(request, actor, "session-two").Should().BeNull();
        service.ReadTarget(request, Actor("another-actor"), "session-one").Should().BeNull();
        service.ReadTarget(request, new ClaimsPrincipal(new ClaimsIdentity()), "session-one").Should().BeNull();
        request.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_and_expired_selections_remain_present_until_explicit_stop()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var provider = new EphemeralDataProtectionProvider();
        var service = Service(db, provider: provider);
        var actor = Actor();
        var invalid = new DefaultHttpContext();
        invalid.Request.Headers.Cookie = ".Booking.Emulation=invalid";

        service.ReadTarget(invalid, actor, "session-one").Should().BeNull();
        service.HasSelection(invalid).Should().BeTrue();
        invalid.Response.Headers.SetCookie.Should().BeEmpty();

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("booking:actor-id", "actor-id"), new Claim("booking:actor-iam", "actor-iam"),
            new Claim("booking:session", "session-one"), new Claim("booking:target-iam", "target"),
        ], "Booking.Emulation")),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }, "Booking.Emulation");
        var format = new TicketDataFormat(provider.CreateProtector("Booking.Emulation.v1", ".Booking.Emulation"));
        var expired = new DefaultHttpContext();
        expired.Request.Headers.Cookie = $".Booking.Emulation={format.Protect(ticket)}";

        service.ReadTarget(expired, actor, "session-one").Should().BeNull();
        service.HasSelection(expired).Should().BeTrue();
        expired.Response.Headers.SetCookie.Should().BeEmpty();
        service.Clear(expired);
        expired.Response.Headers.SetCookie.Should().ContainSingle();
        SetCookieHeaderValue.Parse(expired.Response.Headers.SetCookie.ToString()).Expires.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Local_cookie_suffix_isolated_and_clear_does_not_emit_cookies_when_no_selection_exists()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = "true", ["Auth:LocalCookieSuffix"] = "isolated",
        }).Build();
        var service = Service(db, configuration: configuration);
        var context = new DefaultHttpContext();
        service.Clear(context);
        context.Response.Headers.SetCookie.Should().BeEmpty();

        service.Start(context, Actor(), "session", new User { IamId = "target", Name = "Target" });

        SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString()).Name.Value
            .Should().Be(".Booking.Emulation.isolated");
        context.Request.Headers.Cookie = ".Booking.Emulation=other-sandbox";
        service.HasSelection(context).Should().BeFalse();
    }

    [Fact]
    public void Forced_clear_expires_the_selection_cookie_even_when_a_cross_site_callback_omits_it()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        Service(db).Clear(context, force: true);

        context.Response.Headers.SetCookie.Should().ContainSingle();
        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        cookie.Name.Value.Should().Be(".Booking.Emulation");
        cookie.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
        cookie.Secure.Should().BeTrue();
    }

    private static EmulationService Service(AppDbContext db, bool local = true,
        IConfiguration? configuration = null, IDataProtectionProvider? provider = null)
    {
        configuration ??= new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:UseLocal"] = local.ToString(),
        }).Build();
        var environment = new TestEnvironment();
        var users = new UserService(NullLogger<UserService>.Instance, db, configuration, environment);
        return new EmulationService(db, users, configuration, environment,
            provider ?? new EphemeralDataProtectionProvider(), NullLogger<EmulationService>.Instance);
    }

    private static Person Person() => new()
    {
        IamId = "1000000001", FullName = "Directory Person", Email = "directory@example.test",
        UserId = "person1", IsActiveInIam = true,
    };

    private static ClaimsPrincipal Actor(string iamId = "actor-iam") => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "actor-id"), new Claim("ucdPersonIAMID", iamId)], "Test"));

    private static DefaultHttpContext WithCookie(SetCookieHeaderValue cookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";
        return context;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ConcurrentInsertDbContext : AppDbContext
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly bool _winnerActive;
        private readonly bool _insertWinner;

        public ConcurrentInsertDbContext(DbContextOptions<AppDbContext> options, bool winnerActive, bool insertWinner)
            : base(options)
        {
            _options = options;
            _winnerActive = winnerActive;
            _insertWinner = insertWinner;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var candidate = ChangeTracker.Entries<User>().Single(entry => entry.State == EntityState.Added);
            if (_insertWinner)
            {
                using var concurrent = new AppDbContext(_options);
                concurrent.Users.Add(new User
                {
                    IamId = candidate.Entity.IamId, Name = "Concurrent Sign-in", IsAdmin = true,
                    IsActive = _winnerActive, LastLoginAt = DateTimeOffset.UtcNow,
                });
                await concurrent.SaveChangesAsync(cancellationToken);
            }

            throw new DbUpdateException("Simulated insert failure.");
        }
    }
}

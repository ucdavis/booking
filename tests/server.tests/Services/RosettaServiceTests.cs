using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Services;
using UCD.Rosetta.Client.Core.Configuration;
using RosettaPerson = UCD.Rosetta.Client.Generated.Person;
using ProvisioningStatus = UCD.Rosetta.Client.Generated.Provisioning_status;

namespace Server.Tests.Services;

public class RosettaServiceTests
{
    [Theory]
    [InlineData("1000000001")]
    [InlineData("stored@example.test")]
    [InlineData(" stored-kerb ")]
    public async Task Search_uses_existing_users_without_configuration_or_http(string search)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User
        {
            IamId = "1000000001", Name = "Stored Name", Email = "stored@example.test", Kerberos = "stored-kerb",
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var http = new TestHttpFactory();
        var service = Service(db, http, new RosettaClientOptions());

        var matches = await service.SearchPeopleAsync(search);

        var match = matches.Should().ContainSingle().Subject;
        match.IamId.Should().Be("1000000001");
        match.Name.Should().Be("Stored Name");
        match.Email.Should().Be("stored@example.test");
        match.Kerberos.Should().Be("stored-kerb");
        http.ClientNames.Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Search_orders_and_limits_existing_accounts_without_mutating_them()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "incomplete", Name = "A missing login", Email = "shared@example.test" });
        for (var index = 11; index >= 0; index--)
        {
            db.Users.Add(new User
            {
                IamId = $"{index:D10}", Name = $"Name {index:D2}", Email = "shared@example.test",
                Kerberos = $"person{index}",
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var http = new TestHttpFactory();

        var matches = await Service(db, http).SearchPeopleAsync("shared@example.test");

        matches.Select(person => person.Name).Should().Equal(
            Enumerable.Range(0, 10).Select(index => $"Name {index:D2}"));
        http.ClientNames.Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "storedkerb")]
    [InlineData("", "storedkerb")]
    [InlineData(" ", "storedkerb")]
    [InlineData("stored@ucdavis.edu", null)]
    [InlineData("stored@ucdavis.edu", "")]
    [InlineData("stored@ucdavis.edu", " ")]
    public async Task Search_falls_back_to_directory_when_stored_details_are_incomplete(string? email, string? kerberos)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "1000000001", Name = "Stored Name", Email = email, Kerberos = kerberos });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var http = new TestHttpFactory(Person("1000000001"));

        var match = (await Service(db, http).SearchPeopleAsync("1000000001")).Should().ContainSingle().Subject;

        match.Email.Should().Be("person1@ucdavis.edu");
        match.Kerberos.Should().Be("person1");
        http.Requests.Should().ContainSingle();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Search_checks_Rosetta_instead_of_matching_user_names_or_partial_kerberos()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User
        {
            IamId = "1000000001", Name = "directory", Email = "directory@example.test", Kerberos = "directory1",
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        using var http = new TestHttpFactory();

        var matches = await Service(db, http).SearchPeopleAsync("directory");

        matches.Should().BeEmpty();
        http.Requests.Should().ContainSingle();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" person@ucdavis.edu ", "email", "person@ucdavis.edu")]
    [InlineData(" person.name+test@health.UCDAVIS.EDU ", "email", "person.name+test@health.UCDAVIS.EDU")]
    [InlineData(" 1000000001 ", "iamid", "1000000001")]
    [InlineData(" person1 ", "loginid", "person1")]
    [InlineData("123", "loginid", "123")]
    [InlineData("10000000001", "loginid", "10000000001")]
    [InlineData("person.name_test-1", "loginid", "person.name_test-1")]
    public async Task Search_routes_exact_identifiers_with_a_bounded_request(
        string search, string parameter, string expected)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory();

        await Service(db, http).SearchPeopleAsync(search);

        http.ClientNames.Should().Equal("RosettaClient");
        var request = http.Requests.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be("/api/v1/people");
        var query = QueryHelpers.ParseQuery(request.Query);
        query[parameter].ToString().Should().Be(expected);
        query["limit"].ToString().Should().Be("10");
        new[] { "email", "iamid", "loginid" }.Count(query.ContainsKey).Should().Be(1);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("person*")]
    [InlineData("person%")]
    [InlineData("_person")]
    [InlineData("-person")]
    [InlineData(".person")]
    [InlineData("person.")]
    [InlineData("person name")]
    [InlineData("person\nname")]
    [InlineData("１２３")]
    [InlineData("person@example.test")]
    [InlineData("person@ucdavis.edu.example.test")]
    [InlineData("person@otherucdavis.edu")]
    [InlineData("person @ucdavis.edu")]
    [InlineData("person*@ucdavis.edu")]
    public async Task Search_rejects_unsupported_remote_identifiers_without_configuration_or_http(string search)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory();

        var matches = await Service(db, http, new RosettaClientOptions()).SearchPeopleAsync(search);

        matches.Should().BeEmpty();
        http.ClientNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData(64, 1)]
    [InlineData(65, 0)]
    public async Task Search_enforces_the_remote_login_length_limit(int length, int expectedRequests)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory();

        await Service(db, http).SearchPeopleAsync(new string('a', length));

        http.Requests.Should().HaveCount(expectedRequests);
    }

    [Fact]
    public async Task Search_preserves_existing_accounts_even_when_identifiers_cannot_be_sent_to_rosetta()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var email = new string('a', 129) + "@example.test";
        db.Users.Add(new User { IamId = "legacy%identifier", Name = "Stored Account", Email = email, Kerberos = "legacy" });
        await db.SaveChangesAsync();
        using var http = new TestHttpFactory();
        var service = Service(db, http, new RosettaClientOptions());

        (await service.SearchPeopleAsync("legacy%identifier"))
            .Should().ContainSingle().Which.Name.Should().Be("Stored Account");
        (await service.SearchPeopleAsync(email))
            .Should().ContainSingle().Which.Name.Should().Be("Stored Account");
        http.ClientNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("１２３４５６７８９０")]
    [InlineData("123456789a")]
    [InlineData("person1")]
    [InlineData("%")]
    public async Task Lookup_rejects_non_iam_identifiers_without_configuration_or_http(string iamId)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory();

        var match = await Service(db, http, new RosettaClientOptions()).FindByIamIdAsync(iamId);

        match.Should().BeNull();
        http.ClientNames.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_matches_campus_and_health_emails_but_only_maps_campus_email()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var campus = Person("1000000001", "Campus Match");
        campus.Id.Login_id = "campus1";
        campus.Email.Campus = "match@ucdavis.edu";
        var health = Person("1000000002", "Health Match");
        health.Id.Login_id = "health1";
        health.Email.Campus = "preferred@example.test";
        health.Email.Health = " MATCH@ucdavis.edu ";
        var healthOnly = Person("1000000005", "Health Only");
        healthOnly.Id.Login_id = "health2";
        healthOnly.Email.Campus = null;
        healthOnly.Email.Health = "match@ucdavis.edu";
        var personal = Person("1000000003", "Personal Match");
        personal.Email.Personal = "match@ucdavis.edu";
        var unrelated = Person("1000000004", "Unrelated");
        unrelated.Email.Campus = "notmatch@ucdavis.edu";
        var unidentified = Person("*******", "Missing Identity");
        unidentified.Email.Campus = "match@ucdavis.edu";
        using var http = new TestHttpFactory(campus, health, healthOnly, personal, unrelated, unidentified);

        var matches = await Service(db, http).SearchPeopleAsync("match@ucdavis.edu");

        matches.Select(person => person.IamId).Should().Equal("1000000001", "1000000002");
        matches[0].Email.Should().Be("match@ucdavis.edu");
        matches[1].Email.Should().Be("preferred@example.test");
        matches.Should().OnlyContain(person => person.HasRequiredDetails());
    }

    [Theory]
    [InlineData("campus@ucdavis.edu")]
    [InlineData(null)]
    public async Task Health_email_search_can_start_and_continue_emulation_while_saving_only_campus_email(
        string? campusEmail)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person("1000000001", "Directory Person");
        person.Id.Login_id = "person1";
        person.Email.Campus = campusEmail;
        person.Email.Health = "person1@health.ucdavis.edu";
        person.Provisioning_status = [];
        using var http = new TestHttpFactory(person);
        var rosetta = Service(db, http);
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestEnvironment();
        var users = new UserService(NullLogger<UserService>.Instance, db, configuration, environment, rosetta);
        var emulation = new EmulationService(db, users, configuration, environment,
            new EphemeralDataProtectionProvider(), NullLogger<EmulationService>.Instance, rosetta);

        var candidates = await emulation.SearchAsync(person.Email.Health, default);
        if (campusEmail == null)
        {
            candidates.Should().BeEmpty();
            (await emulation.FindOrCreateTargetAsync(person.Iam_id, default)).User.Should().BeNull();
            db.Users.Should().BeEmpty();
            return;
        }
        var candidate = candidates.Should().ContainSingle().Which;

        candidate.IamId.Should().Be(person.Iam_id);
        candidate.Name.Should().Be("Directory Person");
        candidate.Kerberos.Should().Be("person1");
        candidate.Email.Should().Be(campusEmail);
        candidate.HasUserAccount.Should().BeFalse();

        var result = await emulation.FindOrCreateTargetAsync(candidate.IamId, default);

        result.Error.Should().BeNull();
        result.User.Should().NotBeNull();
        db.ChangeTracker.Clear();
        var saved = await db.Users.SingleAsync();
        saved.Name.Should().Be("Directory Person");
        saved.Email.Should().Be(campusEmail).And.NotBe(person.Email.Health);
        saved.Kerberos.Should().Be("person1");
        saved.LastLoginAt.Should().BeNull();

        var activeTarget = await emulation.GetActiveTargetAsync(candidate.IamId, default);

        activeTarget.Should().NotBeNull();
        activeTarget!.Id.Should().Be(saved.Id);
        var principal = await emulation.CreatePrincipalAsync(activeTarget);
        principal.Identity!.Name.Should().Be("Directory Person");
        (principal.FindFirst("preferred_username")?.Value).Should().Be(campusEmail);
        http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_trims_and_maps_fields_without_persisting_or_exposing_masked_values()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person(" ******* ", " ******* ");
        person.Id.Iam_id = " 1000000001 ";
        person.Id.Login_id = " person1 ";
        person.Name.Lived_first_name = " First ";
        person.Name.Lived_last_name = " Last ";
        person.Email.Campus = " campus@ucdavis.edu ";
        person.Email.Health = " health@example.test ";
        person.Email.Personal = "personal@example.test";
        using var http = new TestHttpFactory(person);

        var matches = await Service(db, http).SearchPeopleAsync("PERSON1");

        var match = matches.Should().ContainSingle().Subject;
        match.IamId.Should().Be("1000000001");
        match.Name.Should().Be("First Last");
        match.Email.Should().Be("campus@ucdavis.edu");
        match.Kerberos.Should().Be("person1");
        db.Users.Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" Display Name ", "Lived", "Legal", "Display Name")]
    [InlineData(null, "Lived", "Legal", "Lived")]
    [InlineData("*******", "*******", " Legal ", "1000000001")]
    [InlineData(null, null, "Legal", "1000000001")]
    [InlineData(null, null, null, "1000000001")]
    public async Task Lookup_uses_display_lived_then_iam_name_fallback_and_ignores_legal_names(
        string? displayName, string? livedName, string? legalName, string expectedName)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person("1000000001", displayName!);
        person.Name.Lived_first_name = livedName;
        person.Name.Legal_first_name = legalName;
        using var http = new TestHttpFactory(person);

        var result = await Service(db, http).FindByIamIdAsync("1000000001");

        result.Should().NotBeNull();
        result!.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("active", null)]
    [InlineData(" ACTIVE ", null)]
    [InlineData("active|active", null)]
    [InlineData("inactive", null)]
    [InlineData("active|inactive", null)]
    [InlineData("pending_removal", null)]
    [InlineData("active|pending_removal", null)]
    [InlineData("*******", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("active", "inactive")]
    [InlineData("active", "*******")]
    [InlineData("inactive", "active")]
    [InlineData("active", "active")]
    public async Task Lookup_ignores_provisioning_status_when_required_identifiers_and_email_are_present(
        string? statuses, string? statusOverride)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person("1000000001");
        person.Id.Login_id = "person1";
        person.Email.Campus = "person1@ucdavis.edu";
        person.Provisioning_status = statuses?.Split('|').Select(status => new ProvisioningStatus
        {
            Primary = status, Primary_override = statusOverride,
        }).ToList() ?? [];
        using var http = new TestHttpFactory(person);

        var result = await Service(db, http).FindByIamIdAsync("1000000001");

        result.Should().NotBeNull();
        result!.HasRequiredDetails().Should().BeTrue();
    }

    [Theory]
    [InlineData(" person1 ", " campus@ucdavis.edu ", null, true, "campus@ucdavis.edu")]
    [InlineData("person1", null, "health@ucdavis.edu", false, null)]
    [InlineData("person1", "*******", " health@ucdavis.edu ", false, null)]
    [InlineData(null, "campus@ucdavis.edu", null, false, "campus@ucdavis.edu")]
    [InlineData(" ", null, "health@ucdavis.edu", false, null)]
    [InlineData("prefix*******suffix", "campus@ucdavis.edu", null, false, "campus@ucdavis.edu")]
    [InlineData("person1", null, null, false, null)]
    [InlineData("person1", " ", " ", false, null)]
    [InlineData("person1", "*******", "*******", false, null)]
    public async Task Lookup_and_search_require_kerberos_and_campus_email_and_never_map_personal_email(
        string? kerberos, string? campusEmail, string? healthEmail, bool expectedMatch, string? expectedEmail)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person("1000000001", " Retained Name ");
        person.Id.Login_id = kerberos;
        person.Email.Campus = campusEmail;
        person.Email.Health = healthEmail;
        person.Email.Personal = "personal@example.test";
        person.Provisioning_status = [new ProvisioningStatus { Primary = "active" }];
        using var http = new TestHttpFactory(person);

        var result = await Service(db, http).FindByIamIdAsync("1000000001");
        var matches = await Service(db, http).SearchPeopleAsync("1000000001");

        if (!expectedMatch)
        {
            result.Should().BeNull();
            matches.Should().BeEmpty();
            return;
        }
        matches.Should().ContainSingle();
        result.Should().NotBeNull();
        result!.Email.Should().Be(expectedEmail);
        result.Name.Should().Be("Retained Name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("*******")]
    public async Task Search_drops_people_without_an_unmasked_iam_id_even_with_kerberos_and_email(string? iamId)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var person = Person(iamId!);
        person.Id.Iam_id = iamId;
        person.Id.Login_id = "person1";
        person.Email.Campus = "person1@ucdavis.edu";
        using var http = new TestHttpFactory(person);

        var matches = await Service(db, http).SearchPeopleAsync("person1");

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_rejects_partial_login_matches_and_deduplicates_and_bounds_exact_matches()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        var people = Enumerable.Range(0, 12).Reverse().Select(index =>
        {
            var person = Person($"{index:D10}", $"Name {index:D2}");
            person.Id.Login_id = "exact";
            return person;
        }).ToList();
        people.Add(people[0]);
        var partial = Person("1000000020", "A partial match");
        partial.Id.Login_id = "exactly";
        people.Add(partial);
        using var http = new TestHttpFactory(people.ToArray());

        var matches = await Service(db, http).SearchPeopleAsync("exact");

        matches.Select(person => person.IamId).Should().Equal(
            Enumerable.Range(0, 10).Select(index => $"{index:D10}"));
    }

    [Fact]
    public async Task Lookup_refreshes_by_exact_iam_even_when_an_account_exists()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        db.Users.Add(new User { IamId = "1000000001", Name = "Stored Name" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var match = Person("1000000001", "Current Person");
        match.Id.Login_id = "person1";
        match.Email.Campus = "person1@ucdavis.edu";
        match.Provisioning_status = [new ProvisioningStatus { Primary = "inactive" }];
        using var http = new TestHttpFactory(Person("1000000002", "Wrong Identity"), match);

        var result = await Service(db, http).FindByIamIdAsync(" 1000000001 ");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Current Person");
        var query = QueryHelpers.ParseQuery(http.Requests.Should().ContainSingle().Subject.Query);
        query["iamid"].ToString().Should().Be("1000000001");
        query["limit"].ToString().Should().Be("2");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lookup_returns_null_for_empty_or_nonmatching_responses(bool includeUnrelatedPerson)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory(includeUnrelatedPerson ? [Person("1000000002")] : []);

        var result = await Service(db, http).FindByIamIdAsync("1000000001");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Lookup_rejects_ambiguous_records_without_disclosing_either_identity()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory(
            Person("1000000001", "Private First Name"), Person("1000000001", "Private Second Name"));

        var action = () => Service(db, http).FindByIamIdAsync("1000000001");

        var error = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.ToString().Should().NotContain("Private").And.NotContain("1000000001");
        error.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_failures_are_errors_instead_of_missing_people_and_hide_response_content(HttpStatusCode status)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("private-response-payload"),
        }));

        var action = () => Service(db, http).FindByIamIdAsync("1000000001");

        var error = (await action.Should().ThrowAsync<HttpRequestException>()).Which;
        error.ToString().Should().NotContain("private-response-payload");
        error.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Network_failures_and_timeouts_hide_upstream_error_details(bool timeout)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory((_, _) => Task.FromException<HttpResponseMessage>(timeout
            ? new TaskCanceledException("private-error-detail")
            : new HttpRequestException("private-error-detail", new Exception("private-inner-detail"))));

        var action = () => Service(db, http).SearchPeopleAsync("person1");

        var error = (await action.Should().ThrowAsync<HttpRequestException>()).Which;
        error.ToString().Should().NotContain("private-error-detail").And.NotContain("private-inner-detail");
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_instead_of_reported_as_an_upstream_failure()
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var cancellation = new CancellationTokenSource();
        using var http = new TestHttpFactory((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var action = () => Service(db, http).FindByIamIdAsync("1000000001", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Empty_inputs_do_not_validate_configuration_or_make_requests(string search)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory();
        var service = Service(db, http, new RosettaClientOptions());

        (await service.SearchPeopleAsync(search)).Should().BeEmpty();
        (await service.FindByIamIdAsync(search)).Should().BeNull();
        http.ClientNames.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_invalid_configuration_is_sanitized_before_requests(bool malformedUrl)
    {
        using var db = TestDbContextFactory.CreateInMemory();
        using var http = new TestHttpFactory();
        var options = Options();
        if (malformedUrl)
        {
            options.BaseUrl = "http://[private-config-detail";
        }
        else
        {
            options.ClientSecret = "";
        }

        var action = () => Service(db, http, options).SearchPeopleAsync("person1");

        var error = (await action.Should().ThrowAsync<HttpRequestException>()).Which;
        error.ToString().Should().NotContain("private-config-detail");
        error.InnerException.Should().BeNull();
        http.Requests.Should().BeEmpty();
    }

    private static RosettaService Service(AppDbContext db, TestHttpFactory http, RosettaClientOptions? options = null)
    {
        return new RosettaService(db, http, Microsoft.Extensions.Options.Options.Create(options ?? Options()));
    }

    private static RosettaClientOptions Options() => new()
    {
        BaseUrl = "https://directory.example.test/api/{version}",
        TokenUrl = "https://directory.example.test/token",
        ClientId = "test-client",
        ClientSecret = "test-placeholder",
    };

    private static RosettaPerson Person(string iamId, string name = "Directory Person") => new()
    {
        Iam_id = iamId,
        Displayname = name,
        Id = new() { Login_id = "person1" },
        Email = new() { Campus = "person1@ucdavis.edu" },
    };

    private sealed class TestHttpFactory : IHttpClientFactory, IDisposable
    {
        private readonly ResponseHandler _handler;
        public List<string> ClientNames { get; } = [];
        public List<Uri> Requests { get; } = [];

        public TestHttpFactory(params RosettaPerson[] people)
            : this((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(people), Encoding.UTF8, "application/json"),
            }))
        {
        }

        public TestHttpFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        {
            _handler = new ResponseHandler((request, cancellationToken) =>
            {
                Requests.Add(request.RequestUri!);
                return response(request, cancellationToken);
            });
        }

        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new HttpClient(_handler, disposeHandler: false);
        }

        public void Dispose() => _handler.Dispose();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return response(request, cancellationToken);
        }
    }
}

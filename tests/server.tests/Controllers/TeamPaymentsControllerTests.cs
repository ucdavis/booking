using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Azure;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Core.Data;
using Server.Core.Domain;
using Server.Helpers;
using Server.Models.Payments;
using Server.Models.Teams;
using Server.Services;

namespace Server.Tests.Controllers;

public class TeamPaymentsControllerTests
{
    private readonly List<string> _calls = [];
    private readonly FakeSecretsService _secrets;
    private readonly FakePaymentsService _payments;

    public TeamPaymentsControllerTests()
    {
        _secrets = new FakeSecretsService(_calls);
        _payments = new FakePaymentsService(_calls);
    }

    [Fact]
    public void Payments_endpoints_require_the_matching_team_policy_and_antiforgery_without_caching()
    {
        var controller = typeof(TeamPaymentsController);
        controller.GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/teams/{teamSlug}/payments");
        controller.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        foreach (var method in new[] { nameof(TeamPaymentsController.GetPayments), nameof(TeamPaymentsController.SavePayments) })
        {
            controller.GetMethod(method)!.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
            controller.GetMethod(method)!.GetCustomAttributes<IgnoreAntiforgeryTokenAttribute>(inherit: true).Should().BeEmpty();
        }
        var get = controller.GetMethod(nameof(TeamPaymentsController.GetPayments))!;
        get.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        get.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(AuthenticationHelper.TeamAccessPolicy);
        var save = controller.GetMethod(nameof(TeamPaymentsController.SavePayments))!;
        save.GetCustomAttribute<HttpPutAttribute>().Should().NotBeNull();
        save.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(AuthenticationHelper.TeamAdminPolicy);
    }

    [Fact]
    public async Task Unconfigured_team_does_not_contact_the_vault_or_payments()
    {
        using var db = CreateDb();
        await SeedTeam(db, configured: false);
        _secrets.Failure = new InvalidOperationException("Missing vault configuration");
        _payments.Failure = new InvalidOperationException("Missing Payments configuration");

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.Status.Should().Be("unconfigured");
        response.MaskedApiKey.Should().BeNull();
        response.PaymentsTeamName.Should().BeNull();
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TeamRole.Admin, false, true)]
    [InlineData(TeamRole.Editor, false, false)]
    [InlineData(TeamRole.Viewer, false, false)]
    [InlineData(null, true, true)]
    public async Task Team_view_checks_the_stored_key_and_only_administrators_receive_its_mask(
        TeamRole? role, bool siteAdmin, bool receivesMask)
    {
        using var db = CreateDb();
        await SeedTeam(db, role, siteAdmin);

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.Status.Should().Be("valid");
        response.PaymentsTeamName.Should().Be("Payments Team");
        response.PaymentsTeamSlug.Should().Be("payments-team");
        response.MaskedApiKey.Should().Be(receivesMask ? "ABC******XYZ" : null);
        _calls.Should().Equal("get-secret", "validate-key");
        _secrets.ReadNames.Should().Equal("existing-secret-name");
        _payments.ValidatedKeys.Should().Equal(_secrets.StoredKey);
        JsonSerializer.Serialize(response).Should().NotContain(_secrets.StoredKey).And.NotContain("existing-secret-name");
    }

    [Theory]
    [InlineData("ABCDEF", "******")]
    [InlineData("ABC", "***")]
    [InlineData("A", "*")]
    [InlineData("ABCDEFG", "ABC*EFG")]
    public async Task Short_keys_are_never_exposed_by_overlapping_prefix_and_suffix(string key, string mask)
    {
        using var db = CreateDb();
        await SeedTeam(db);
        _secrets.StoredKey = key;

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.MaskedApiKey.Should().Be(mask);
    }

    [Fact]
    public async Task Disabled_key_is_reported_without_mutating_the_saved_connection()
    {
        using var db = CreateDb();
        var team = await SeedTeam(db);
        _payments.Team = null;

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.Status.Should().Be("invalid");
        response.Message.Should().Contain("disabled");
        response.PaymentsTeamSlug.Should().Be("payments-team");
        response.PaymentsTeamName.Should().BeNull();
        response.MaskedApiKey.Should().Be("ABC******XYZ");
        team.PaymentsApiKeySecretName.Should().Be("existing-secret-name");
        _calls.Should().Equal("get-secret", "validate-key");
    }

    [Fact]
    public async Task An_existing_secret_without_a_saved_slug_can_still_be_verified_and_displayed()
    {
        using var db = CreateDb();
        var team = await SeedTeam(db);
        team.PaymentsTeamSlug = null;
        await db.SaveChangesAsync();
        _calls.Clear();

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.Status.Should().Be("valid");
        response.PaymentsTeamSlug.Should().Be("payments-team");
        response.PaymentsTeamName.Should().Be("Payments Team");
        response.MaskedApiKey.Should().Be("ABC******XYZ");
        team.PaymentsTeamSlug.Should().BeNull();
        _calls.Should().Equal("get-secret", "validate-key");
    }

    [Fact]
    public async Task A_different_upstream_team_is_reported_as_a_mismatch_without_changing_the_connection()
    {
        using var db = CreateDb();
        var team = await SeedTeam(db);
        _payments.Team = new PaymentsTeam { Name = "Different Payments Team", Slug = "different" };

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.Status.Should().Be("invalid");
        response.Message.Should().Contain("different Payments team");
        response.PaymentsTeamSlug.Should().Be("payments-team");
        response.PaymentsTeamName.Should().BeNull();
        team.PaymentsTeamSlug.Should().Be("payments-team");
        _calls.Should().Equal("get-secret", "validate-key");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Vault_and_upstream_failures_show_unavailable_without_exposing_exception_details(bool vaultFailure)
    {
        using var db = CreateDb();
        await SeedTeam(db);
        const string failureDetail = "DO-NOT-EXPOSE-TEST-DETAIL";
        if (vaultFailure)
        {
            _secrets.Failure = new RequestFailedException(403, failureDetail);
        }
        else
        {
            _payments.Failure = new HttpRequestException(failureDetail);
        }

        var response = ReadValue(await CreateController(db).GetPayments("team-a"));

        response.Status.Should().Be("unavailable");
        response.Message.Should().NotContain(failureDetail);
        response.PaymentsTeamSlug.Should().Be("payments-team");
        _calls.Should().NotContain("save-db").And.NotContain("set-secret");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(TeamRole.Editor, false)]
    [InlineData(TeamRole.Viewer, false)]
    [InlineData(TeamRole.Admin, true)]
    public async Task Non_admin_or_inactive_users_cannot_replace_a_key(TeamRole? role, bool inactive)
    {
        using var db = CreateDb();
        await SeedTeam(db, role, active: !inactive);

        var response = await CreateController(db).SavePayments("team-a", new SaveTeamPaymentsRequest { ApiKey = "replacement-test-key" });

        response.Result.Should().BeOfType<ForbidResult>();
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Unrelated_team_or_revoked_access_cannot_read_or_change_a_connection()
    {
        using var db = CreateDb();
        await SeedTeam(db);
        db.Teams.Add(new Team { Name = "Other", Slug = "other" });
        await db.SaveChangesAsync();
        _calls.Clear();

        (await CreateController(db).GetPayments("other")).Result.Should().BeOfType<NotFoundObjectResult>();
        (await CreateController(db).SavePayments("other", new SaveTeamPaymentsRequest { ApiKey = "replacement-test-key" }))
            .Result.Should().BeOfType<ForbidResult>();
        (await CreateController(db, authenticated: false).GetPayments("team-a")).Result.Should().BeOfType<NotFoundObjectResult>();
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc\ndef")]
    [InlineData("\r\nabc")]
    [InlineData("abc\0def")]
    public async Task Empty_or_control_character_keys_are_rejected_before_any_external_call(string? key)
    {
        using var db = CreateDb();
        await SeedTeam(db);

        var response = await CreateController(db).SavePayments("team-a", new SaveTeamPaymentsRequest { ApiKey = key! });

        response.Result.Should().BeOfType<BadRequestObjectResult>();
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_key_is_rejected_before_any_external_call()
    {
        using var db = CreateDb();
        await SeedTeam(db);

        var response = await CreateController(db).SavePayments("team-a", new SaveTeamPaymentsRequest { ApiKey = new string('x', 4097) });

        response.Result.Should().BeOfType<BadRequestObjectResult>();
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_key_never_writes_to_the_vault_or_database(bool configured)
    {
        using var db = CreateDb();
        var team = await SeedTeam(db, configured: configured);
        var originalUpdatedAt = team.UpdatedAt;
        _payments.Team = null;

        var response = await CreateController(db).SavePayments("team-a", new SaveTeamPaymentsRequest { ApiKey = "invalid-test-key" });

        response.Result.Should().BeOfType<BadRequestObjectResult>();
        _calls.Should().Equal("validate-key");
        team.PaymentsApiKeySecretName.Should().Be(configured ? "existing-secret-name" : null);
        team.PaymentsTeamSlug.Should().Be(configured ? "payments-team" : null);
        team.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrators_save_only_after_validation_and_use_a_new_secret_for_every_replacement(bool siteAdmin)
    {
        using var db = CreateDb();
        var team = await SeedTeam(db, role: siteAdmin ? null : TeamRole.Admin, siteAdmin: siteAdmin, configured: false);

        var first = ReadValue(await CreateController(db).SavePayments("team-a",
            new SaveTeamPaymentsRequest { ApiKey = "  ABCsecretXYZ  " }));
        var firstSecretName = team.PaymentsApiKeySecretName;

        _calls.Should().Equal("validate-key", "set-secret", "save-db");
        _payments.ValidatedKeys.Should().Equal("ABCsecretXYZ");
        _secrets.Writes.Single().Key.Should().Be(firstSecretName);
        _secrets.Writes.Single().Value.Should().Be("ABCsecretXYZ");
        firstSecretName.Should().StartWith($"team-{team.Id}-payments-");
        team.PaymentsTeamSlug.Should().Be("payments-team");
        first.Status.Should().Be("valid");
        first.PaymentsTeamName.Should().Be("Payments Team");
        first.MaskedApiKey.Should().Be("ABC******XYZ");
        JsonSerializer.Serialize(first).Should().NotContain("ABCsecretXYZ").And.NotContain(firstSecretName!);

        _calls.Clear();
        _payments.Team = new PaymentsTeam { Name = "New Payments Team", Slug = "new-payments-team" };
        var second = ReadValue(await CreateController(db).SavePayments("team-a",
            new SaveTeamPaymentsRequest { ApiKey = "NEWsecretEND" }));

        _calls.Should().Equal("validate-key", "set-secret", "save-db");
        team.PaymentsApiKeySecretName.Should().NotBe(firstSecretName);
        _secrets.Writes.Should().HaveCount(2);
        _secrets.Writes[firstSecretName!].Should().Be("ABCsecretXYZ");
        _secrets.Writes[team.PaymentsApiKeySecretName!].Should().Be("NEWsecretEND");
        team.PaymentsTeamSlug.Should().Be("new-payments-team");
        second.PaymentsTeamName.Should().Be("New Payments Team");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_or_vault_outage_preserves_the_existing_connection(bool vaultFailure)
    {
        using var db = CreateDb();
        var team = await SeedTeam(db);
        var originalUpdatedAt = team.UpdatedAt;
        if (vaultFailure)
        {
            _secrets.Failure = new RequestFailedException(503, "Do not expose vault details");
        }
        else
        {
            _payments.Failure = new HttpRequestException("Do not expose upstream details");
        }

        var result = await CreateController(db).SavePayments("team-a", new SaveTeamPaymentsRequest { ApiKey = "replacement-test-key" });

        var response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        response.Value.Should().BeOfType<string>().Subject.Should().NotContain("Do not expose");
        _calls.Should().NotContain("save-db");
        _secrets.Writes.Should().BeEmpty();
        team.PaymentsTeamSlug.Should().Be("payments-team");
        team.PaymentsApiKeySecretName.Should().Be("existing-secret-name");
        team.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Fact]
    public async Task Database_failure_leaves_the_previous_reference_and_returns_a_safe_failure()
    {
        using var db = CreateDb();
        var team = await SeedTeam(db);
        var originalUpdatedAt = team.UpdatedAt;
        db.FailSave = true;
        _payments.Team = new PaymentsTeam { Name = "Replacement", Slug = "replacement" };

        var result = await CreateController(db).SavePayments("team-a", new SaveTeamPaymentsRequest { ApiKey = "replacement-test-key" });

        var response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        response.Value.Should().BeOfType<string>().Subject.Should()
            .Be("The Payments save could not be confirmed. Check the connection before trying again.");
        _calls.Should().Equal("validate-key", "set-secret", "save-db");
        _secrets.Writes.Should().ContainSingle();
        _secrets.Writes.Should().NotContainKey("existing-secret-name");
        team.PaymentsTeamSlug.Should().Be("payments-team");
        team.PaymentsApiKeySecretName.Should().Be("existing-secret-name");
        team.UpdatedAt.Should().Be(originalUpdatedAt);
        db.ChangeTracker.HasChanges().Should().BeFalse();
        db.ChangeTracker.Clear();
        (await db.Teams.SingleAsync()).PaymentsApiKeySecretName.Should().Be("existing-secret-name");
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_saving_a_key()
    {
        using var db = CreateDb();
        await SeedTeam(db);
        using var cancellation = new CancellationTokenSource();
        _payments.BeforeValidate = cancellation.Cancel;
        _payments.Failure = new OperationCanceledException(cancellation.Token);

        var save = () => CreateController(db).SavePayments("team-a",
            new SaveTeamPaymentsRequest { ApiKey = "replacement-test-key" }, cancellation.Token);

        await save.Should().ThrowAsync<OperationCanceledException>();
        _calls.Should().Equal("validate-key");
        _secrets.Writes.Should().BeEmpty();
    }

    private RecordingDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TeamPayments_{Guid.NewGuid():N}").Options;
        return new RecordingDbContext(options, _calls);
    }

    private async Task<Team> SeedTeam(RecordingDbContext db, TeamRole? role = TeamRole.Admin,
        bool siteAdmin = false, bool configured = true, bool active = true)
    {
        var team = new Team
        {
            Name = "Team A", Slug = "team-a", PaymentsTeamSlug = configured ? "payments-team" : null,
            PaymentsApiKeySecretName = configured ? "existing-secret-name" : null,
            UpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var user = new User { IamId = "member-iam", Name = "Member", IsAdmin = siteAdmin, IsActive = active };
        db.Teams.Add(team);
        db.Users.Add(user);
        if (role != null)
        {
            db.TeamPermissions.Add(new TeamPermission { Team = team, User = user, Role = role.Value });
        }
        await db.SaveChangesAsync();
        _calls.Clear();
        return team;
    }

    private TeamPaymentsController CreateController(AppDbContext db, bool authenticated = true)
        => new(db, new TeamAccessService(db), _secrets, _payments)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("ucdPersonIAMID", "member-iam")], authenticated ? "Test" : null)),
                },
            },
        };

    private static TeamPaymentsResponse ReadValue(ActionResult<TeamPaymentsResponse> result)
        => result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<TeamPaymentsResponse>().Subject;

    private sealed class RecordingDbContext(DbContextOptions<AppDbContext> options, List<string> calls) : AppDbContext(options)
    {
        public bool FailSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("save-db");
            if (FailSave)
            {
                throw new DbUpdateException("Do not expose database details");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FakeSecretsService(List<string> calls) : ISecretsService
    {
        public string StoredKey { get; set; } = "ABCsecretXYZ";
        public Exception? Failure { get; set; }
        public List<string> ReadNames { get; } = [];
        public Dictionary<string, string> Writes { get; } = [];

        public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        {
            calls.Add("get-secret");
            ReadNames.Add(secretName);
            if (Failure != null)
            {
                throw Failure;
            }
            return Task.FromResult(StoredKey);
        }

        public Task SetSecretAsync(string secretName, string value, CancellationToken cancellationToken = default)
        {
            calls.Add("set-secret");
            if (Failure != null)
            {
                throw Failure;
            }
            Writes.Add(secretName, value);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentsService(List<string> calls) : IPaymentsService
    {
        public PaymentsTeam? Team { get; set; } = new() { Name = "Payments Team", Slug = "payments-team" };
        public Exception? Failure { get; set; }
        public Action? BeforeValidate { get; set; }
        public List<string> ValidatedKeys { get; } = [];

        public Task<PaymentsTeam?> GetTeamForApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            calls.Add("validate-key");
            ValidatedKeys.Add(apiKey);
            BeforeValidate?.Invoke();
            if (Failure != null)
            {
                throw Failure;
            }
            return Task.FromResult(Team);
        }

        public Task<PaymentsTeam?> GetTeamAsync(string secretName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Controller should read each key once to validate and mask it.");
    }
}

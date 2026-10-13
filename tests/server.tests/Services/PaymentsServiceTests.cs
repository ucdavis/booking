using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Models.Payments;
using Server.Services;

namespace Server.Tests.Services;

public class PaymentsServiceTests
{
    private const string TestKey = "synthetic-payments-key-for-tests";

    [Theory]
    [InlineData("https://payments.example.test", "https://payments.example.test/api/team/")]
    [InlineData("https://payments.example.test/", "https://payments.example.test/api/team/")]
    [InlineData("https://payments.example.test/payments/", "https://payments.example.test/payments/api/team/")]
    public async Task Verification_uses_the_raw_key_on_only_the_request_and_maps_the_team(string baseUrl, string expectedUrl)
    {
        using var http = new TestHttpFactory((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.Should().Be(expectedUrl);
            request.Headers.GetValues("Authorization").Should().Equal(TestKey);
            request.Headers.Accept.Should().ContainSingle().Which.MediaType.Should().Be("application/json");
            return Task.FromResult(TeamResponse());
        });
        var secrets = new TestSecrets();

        var result = await Service(http, secrets, baseUrl).GetTeamForApiKeyAsync(TestKey);

        result!.Name.Should().Be("Payments team");
        result.Slug.Should().Be("payments-team");
        http.Names.Should().Equal(PaymentsService.ClientName);
        secrets.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task Stored_key_lookup_reads_the_latest_secret_each_time_and_can_run_without_a_user_session()
    {
        using var http = new TestHttpFactory((request, _) =>
        {
            request.Headers.GetValues("Authorization").Should().Equal(TestKey);
            return Task.FromResult(TeamResponse());
        });
        var secrets = new TestSecrets();
        var service = Service(http, secrets);
        using var cancellation = new CancellationTokenSource();

        await service.GetTeamAsync("team-42-payments", cancellation.Token);
        await service.GetTeamAsync("team-42-payments", cancellation.Token);

        secrets.Reads.Should().Equal("team-42-payments", "team-42-payments");
        secrets.LastToken.Should().Be(cancellation.Token);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Invalid_or_disabled_keys_are_distinct_from_connection_failures(HttpStatusCode status)
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        (await Service(http).GetTeamForApiKeyAsync(TestKey)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Other_statuses_fail_without_retaining_upstream_response_bodies(HttpStatusCode status)
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(TestKey),
        }));

        var verify = () => Service(http).GetTeamForApiKeyAsync(TestKey);

        var error = (await verify.Should().ThrowAsync<HttpRequestException>()).Which;
        error.ToString().Should().NotContain(TestKey);
        error.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"name\":\"Example\"}")]
    [InlineData("{\"name\":\"\",\"slug\":\"example\"}")]
    [InlineData("{\"name\":\"Example\",\"slug\":\" \"}")]
    [InlineData("{\"name\":\"Example\\nname\",\"slug\":\"example\"}")]
    [InlineData("{\"name\":\"Example\",\"slug\":\"example\\nslug\"}")]
    [InlineData("<html>Login</html>")]
    public async Task Malformed_or_incomplete_team_responses_cannot_validate_a_key(string body)
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

        var verify = () => Service(http).GetTeamForApiKeyAsync(TestKey);

        var error = (await verify.Should().ThrowAsync<HttpRequestException>()).Which;
        error.InnerException.Should().BeNull();
        error.ToString().Should().NotContain(body);
    }

    [Fact]
    public async Task Slug_that_cannot_fit_the_existing_column_is_rejected_before_saving()
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(TeamResponse(new string('s', 101))));

        var verify = () => Service(http).GetTeamForApiKeyAsync(TestKey);

        await verify.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Oversized_team_name_is_rejected_as_invalid_upstream_metadata()
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"name":"{{new string('n', 201)}}","slug":"example"}""", Encoding.UTF8, "application/json"),
        }));

        var verify = () => Service(http).GetTeamForApiKeyAsync(TestKey);

        await verify.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unsafe\r\nHeader:value")]
    public async Task Empty_or_control_character_keys_never_make_a_request(string key)
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(TeamResponse()));

        (await Service(http).GetTeamForApiKeyAsync(key)).Should().BeNull();
        http.Names.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://payments.example.test")]
    [InlineData("https://user:synthetic-secret@payments.example.test")]
    [InlineData("https://payments.example.test?key=synthetic-secret")]
    [InlineData("https://payments.example.test#fragment")]
    [InlineData("not a URL")]
    public async Task Invalid_configuration_is_rejected_without_including_the_value(string baseUrl)
    {
        using var http = new TestHttpFactory((_, _) => Task.FromResult(TeamResponse()));

        var verify = () => Service(http, baseUrl: baseUrl).GetTeamForApiKeyAsync(TestKey);

        var error = (await verify.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("Payments:BaseUrl");
        error.ToString().Should().NotContain("synthetic-secret");
        http.Names.Should().BeEmpty();
    }

    [Fact]
    public async Task Transport_failures_do_not_retain_credentials_from_exception_messages()
    {
        using var http = new TestHttpFactory((_, _) => throw new HttpRequestException(TestKey));

        var verify = () => Service(http).GetTeamForApiKeyAsync(TestKey);

        var error = (await verify.Should().ThrowAsync<HttpRequestException>()).Which;
        error.ToString().Should().NotContain(TestKey);
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Timeouts_are_unavailable_but_caller_cancellation_is_propagated()
    {
        using var http = new TestHttpFactory((_, _) => throw new TaskCanceledException(TestKey));
        var service = Service(http);
        var timedOut = () => service.GetTeamForApiKeyAsync(TestKey);

        var error = (await timedOut.Should().ThrowAsync<HttpRequestException>()).Which;
        error.Message.Should().Contain("timed out");
        error.ToString().Should().NotContain(TestKey);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => service.GetTeamForApiKeyAsync(TestKey, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        http.Names.Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_team_requests_keep_their_credentials_separate()
    {
        var arrived = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new TestHttpFactory(async (request, _) =>
        {
            var key = request.Headers.GetValues("Authorization").Single();
            if (Interlocked.Increment(ref arrived) == 2)
            {
                ready.SetResult();
            }
            await ready.Task;
            return TeamResponse(key);
        });
        var service = Service(http);

        var teams = await Task.WhenAll(service.GetTeamForApiKeyAsync("first-team-key"),
            service.GetTeamForApiKeyAsync("second-team-key"));

        teams.Select(team => team!.Slug).Should().Equal("first-team-key", "second-team-key");
    }

    [Fact]
    public void Registration_is_lazy_and_configures_a_client_without_redirects_or_cookies()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSecretsService(configuration);
        services.AddPaymentsService(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<IPaymentsService>().Should().BeOfType<PaymentsService>();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(PaymentsService.ClientName);
        while (handler is DelegatingHandler)
        {
            handler = ((DelegatingHandler)handler).InnerHandler!;
        }
        var transport = handler.Should().BeOfType<HttpClientHandler>().Subject;
        transport.AllowAutoRedirect.Should().BeFalse();
        transport.UseCookies.Should().BeFalse();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PaymentsService.ClientName);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        client.MaxResponseContentBufferSize.Should().Be(64 * 1024);
    }

    private static PaymentsService Service(TestHttpFactory http, TestSecrets? secrets = null,
        string baseUrl = "https://payments.example.test")
        => new(http, secrets ?? new TestSecrets(), Options.Create(new PaymentsOptions { BaseUrl = baseUrl }));

    private static HttpResponseMessage TeamResponse(string slug = "payments-team") => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"name":" Payments team ","slug":"{{slug}}"}""", Encoding.UTF8, "application/json"),
    };

    private sealed class TestSecrets : ISecretsService
    {
        public List<string> Reads { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        {
            Reads.Add(secretName);
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TestKey);
        }

        public Task SetSecretAsync(string secretName, string value, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Verification must never save a secret.");
    }

    private sealed class TestHttpFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<string> Names { get; } = new();

        public HttpClient CreateClient(string name)
        {
            Names.Enqueue(name);
            var client = new HttpClient(this, disposeHandler: false);
            client.DefaultRequestHeaders.Contains("Authorization").Should().BeFalse();
            return client;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}

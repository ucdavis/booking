using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AggieEnterpriseApi;
using AggieEnterpriseApi.Serializers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Services;

namespace Server.Tests.Services;

public class AggieEnterpriseServiceTests
{
    private const string GlChart = "3110-13U20-ADNO003-410000-43-000-0000000000-000000-0000-000000-000000";
    private const string PpmChart = "K30APSD227-TASK01-APLS002-770000";
    private const string FullPpmChart = "K30APSD227-TASK01-APLS002-770000-K381C99-27666";

    [Fact]
    public void Registration_allows_startup_without_configuration_or_remote_access()
    {
        var services = new ServiceCollection();
        services.AddAggieEnterpriseService(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        provider.GetRequiredService<IAggieEnterpriseService>().Should().BeOfType<AggieEnterpriseService>();
    }

    [Theory]
    [InlineData(GlChart, "GL", "GlValidateChartstring")]
    [InlineData(PpmChart, "PPM", "PpmSegmentStringValidate")]
    [InlineData(FullPpmChart, "PPM", "PpmSegmentStringValidate")]
    public async Task Valid_chart_strings_use_the_matching_simple_validation_query(
        string chartString, string chartType, string operation)
    {
        using var client = new TestClient(Response(chartType));

        var result = await client.Service.ValidateChartStringAsync(chartString);

        result.ChartString.Should().Be(chartString);
        result.ChartType.Should().Be(chartType);
        result.IsValid.Should().BeTrue();
        result.Messages.Should().BeEmpty();
        result.Message.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Warning.Should().BeEmpty();
        var request = client.Requests.Should().ContainSingle().Subject;
        request.GetProperty("operationName").GetString().Should().Be(operation);
        var variables = request.GetProperty("variables");
        variables.GetProperty("segmentString").GetString().Should().Be(chartString);
        if (chartType == "GL")
        {
            variables.GetProperty("validateCVRs").GetBoolean().Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(GlChart, "GL")]
    [InlineData(PpmChart, "PPM")]
    public async Task Validation_errors_and_warnings_remain_separate_and_join_into_their_strings(
        string chartString, string chartType)
    {
        using var client = new TestClient(Response(chartType, valid: false,
            messages: ["The project is closed.", "The department is invalid."],
            warnings: [("project", "Project ends soon."), ("account", "Account is restricted.")]));

        var result = await client.Service.ValidateChartStringAsync(chartString);

        result.ChartString.Should().Be(chartString);
        result.ChartType.Should().Be(chartType);
        result.IsValid.Should().BeFalse();
        result.Messages.Should().Equal("The project is closed.", "The department is invalid.");
        result.Message.Should().Be("The project is closed. The department is invalid.");
        result.Warnings.Should().Equal("project: Project ends soon.", "account: Account is restricted.");
        result.Warning.Should().Be("project: Project ends soon. account: Account is restricted.");
    }

    [Theory]
    [InlineData(GlChart, "GL")]
    [InlineData(PpmChart, "PPM")]
    public async Task Warnings_alone_do_not_invalidate_a_chart_string(string chartString, string chartType)
    {
        using var client = new TestClient(Response(chartType,
            warnings: [("project", "Project ends soon.")]));

        var result = await client.Service.ValidateChartStringAsync(chartString);

        result.IsValid.Should().BeTrue();
        result.Messages.Should().BeEmpty();
        result.Warnings.Should().Equal("project: Project ends soon.");
    }

    [Theory]
    [InlineData(GlChart, "GL")]
    [InlineData(PpmChart, "PPM")]
    public async Task Graphql_errors_are_retained_alongside_partial_validation_results(
        string chartString, string chartType)
    {
        using var client = new TestClient(Response(chartType,
            messages: ["The department is invalid."],
            warnings: [("project", "Project ends soon.")],
            errors: ["Validation was incomplete.", "A segment lookup failed."]));

        var result = await client.Service.ValidateChartStringAsync(chartString);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().BeEquivalentTo(new[]
        {
            "The department is invalid.", "Validation was incomplete.", "A segment lookup failed.",
        });
        result.Message.Should().Be(string.Join(" ", result.Messages));
        result.Warnings.Should().Equal("project: Project ends soon.");
    }

    [Theory]
    [InlineData(GlChart, "GL")]
    [InlineData(PpmChart, "PPM")]
    public async Task An_invalid_response_without_errors_still_explains_the_failure(
        string chartString, string chartType)
    {
        using var client = new TestClient(Response(chartType, valid: false));

        var result = await client.Service.ValidateChartStringAsync(chartString);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        result.Message.Should().Be(string.Join(" ", result.Messages));
    }

    [Fact]
    public async Task Error_messages_override_an_inconsistent_valid_flag()
    {
        using var client = new TestClient(Response("GL", messages: ["The account is invalid."]));

        var result = await client.Service.ValidateChartStringAsync(GlChart);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Equal("The account is invalid.");
    }

    [Fact]
    public async Task A_graphql_error_without_a_message_still_invalidates_partial_data()
    {
        using var client = new TestClient(Response("GL", errors: [""]));

        var result = await client.Service.ValidateChartStringAsync(GlChart);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_valid_gl_response_may_omit_warnings()
    {
        var response = JsonNode.Parse(Response("GL"))!;
        response["data"]!["glValidateChartstring"]!["warnings"] = null;
        using var client = new TestClient(response.ToJsonString());

        var result = await client.Service.ValidateChartStringAsync(GlChart);

        result.IsValid.Should().BeTrue();
        result.Messages.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.Warning.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("not-a-chart-string")]
    [InlineData("3110-13U20-ADNO003")]
    public async Task Malformed_inputs_preserve_the_input_and_do_not_resolve_the_remote_client(string chartString)
    {
        var resolutions = 0;
        var service = new AggieEnterpriseService(() =>
        {
            resolutions++;
            throw new InvalidOperationException("The remote client must not be resolved.");
        });

        var result = await service.ValidateChartStringAsync(chartString);

        result.ChartString.Should().Be(chartString);
        result.ChartType.Should().BeNull();
        result.IsValid.Should().BeFalse();
        result.Messages.Should().NotBeEmpty();
        result.Message.Should().Be(string.Join(" ", result.Messages));
        result.Warnings.Should().BeEmpty();
        resolutions.Should().Be(0);
    }

    [Theory]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("private-malformed-response")]
    public async Task Unusable_responses_return_a_failure_without_raw_response_details(string response)
    {
        using var client = new TestClient(response);

        var result = await client.Service.ValidateChartStringAsync(GlChart);

        result.IsValid.Should().BeFalse();
        result.ChartType.Should().Be("GL");
        result.Messages.Should().NotBeEmpty();
        result.Message.Should().Be(string.Join(" ", result.Messages));
        result.Message.Should().NotContain("private-malformed-response");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Network_failures_and_timeouts_return_safe_failure_messages(bool timeout)
    {
        using var client = new TestClient((_, _) => Task.FromException<HttpResponseMessage>(timeout
            ? new TaskCanceledException("private-upstream-error")
            : new HttpRequestException("private-upstream-error")));

        var result = await client.Service.ValidateChartStringAsync(GlChart);

        result.IsValid.Should().BeFalse();
        result.ChartString.Should().Be(GlChart);
        result.ChartType.Should().Be("GL");
        result.Messages.Should().NotBeEmpty();
        result.Message.Should().NotContain("private-upstream-error");
    }

    [Fact]
    public async Task Graphql_errors_without_data_are_reported()
    {
        using var client = new TestClient("""
            {"errors":[{"message":"The chart string could not be validated."}]}
            """);

        var result = await client.Service.ValidateChartStringAsync(PpmChart);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().Contain("The chart string could not be validated.");
    }

    [Fact]
    public async Task Caller_cancellation_propagates_through_the_generated_client()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new TestClient((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var action = () => client.Service.ValidateChartStringAsync(GlChart, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Registered_service_uses_an_overridden_client_without_requiring_configuration()
    {
        using var client = new TestClient(Response("PPM"));
        var services = new ServiceCollection();
        services.AddAggieEnterpriseService(new ConfigurationBuilder().Build());
        services.AddSingleton(client.Client);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAggieEnterpriseService>()
            .ValidateChartStringAsync(PpmChart);

        result.IsValid.Should().BeTrue();
        client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Missing_configuration_returns_a_validation_failure_model()
    {
        var services = new ServiceCollection();
        services.AddAggieEnterpriseService(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAggieEnterpriseService>()
            .ValidateChartStringAsync(GlChart);

        result.ChartString.Should().Be(GlChart);
        result.ChartType.Should().Be("GL");
        result.IsValid.Should().BeFalse();
        result.Messages.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        result.Message.Should().Be(string.Join(" ", result.Messages));
        result.Warnings.Should().BeEmpty();
    }

    private static string Response(string chartType, bool valid = true, string[]? messages = null,
        (string SegmentName, string Warning)[]? warnings = null, string[]? errors = null)
    {
        var isGl = chartType == "GL";
        var payload = new Dictionary<string, object?>
        {
            ["__typename"] = isGl ? "GlValidateChartSegmentsOutput" : "PpmSegmentsValidateOutput",
            ["validationResponse"] = new
            {
                __typename = "ValidationResponse",
                valid,
                errorMessages = messages,
                messageProperties = Array.Empty<string>(),
            },
            ["warnings"] = (warnings ?? []).Select(warning => new
            {
                __typename = "SegmentWarning",
                segmentName = warning.SegmentName,
                warning = warning.Warning,
            }).ToArray(),
        };
        if (isGl)
        {
            payload["segments"] = new
            {
                __typename = "GlSegments", entity = "3110", fund = "13U20", department = "ADNO003",
                account = "410000", purpose = "43", program = "000", project = "0000000000", activity = "000000",
            };
            payload["segmentNames"] = new
            {
                __typename = "GlSegmentNames", entityName = "Entity", fundName = "Fund", departmentName = "Department",
                accountName = "Account", purposeName = "Purpose", programName = "Program", projectName = "Project",
                activityName = "Activity",
            };
            payload["completeChartstring"] = GlChart;
            payload["codeCombinationId"] = null;
            payload["endDates"] = Array.Empty<object>();
        }
        else
        {
            payload["segments"] = new
            {
                __typename = "PpmSegments", project = "K30APSD227", task = "TASK01", organization = "APLS002",
                expenditureType = "770000", award = "K381C99", fundingSource = "27666",
            };
            payload["segmentString"] = FullPpmChart;
        }

        var response = new Dictionary<string, object?>
        {
            ["data"] = new Dictionary<string, object?>
            {
                ["__typename"] = "Query",
                [isGl ? "glValidateChartstring" : "ppmSegmentStringValidate"] = payload,
            },
        };
        if (errors != null)
        {
            response["errors"] = errors.Select(error => new { message = error }).ToArray();
        }

        return JsonSerializer.Serialize(response);
    }

    private sealed class TestClient : IDisposable
    {
        private readonly ServiceProvider _provider;
        public IAggieEnterpriseClient Client { get; }
        public AggieEnterpriseService Service { get; }
        public List<JsonElement> Requests { get; } = [];

        public TestClient(string response)
            : this((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            }))
        {
        }

        public TestClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        {
            var handler = new ResponseHandler(async (request, token) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Requests.Add(body.RootElement.Clone());
                return await response(request, token);
            });
            var services = new ServiceCollection();
            services.AddSerializer<PositiveIntSerializer>();
            services.AddSerializer<NonNegativeIntSerializer>();
            services.AddSerializer<NonPositiveIntSerializer>();
            services.AddSerializer<NonNegativeFloatSerializer>();
            services.AddAggieEnterpriseClient().ConfigureHttpClient(
                http => http.BaseAddress = new Uri("https://aggie-enterprise.example.test/graphql"),
                builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
            _provider = services.BuildServiceProvider();
            Client = _provider.GetRequiredService<IAggieEnterpriseClient>();
            Service = new AggieEnterpriseService(Client);
        }

        public void Dispose() => _provider.Dispose();
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

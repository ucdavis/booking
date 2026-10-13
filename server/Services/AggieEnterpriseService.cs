using AggieEnterpriseApi;
using AggieEnterpriseApi.Validation;
using Server.Models.AggieEnterprise;

namespace Server.Services;

public interface IAggieEnterpriseService
{
    Task<ChartStringValidationResult> ValidateChartStringAsync(string chartString, CancellationToken cancellationToken = default);
}

public sealed class AggieEnterpriseService : IAggieEnterpriseService
{
    private const string ServiceFailureMessage = "Aggie Enterprise chart string validation failed. Check the AggieEnterprise configuration and service availability.";
    private readonly Lazy<IAggieEnterpriseClient> _client;

    public AggieEnterpriseService(IAggieEnterpriseClient client) : this(() => client) { }

    public AggieEnterpriseService(Func<IAggieEnterpriseClient> createClient)
    {
        _client = new Lazy<IAggieEnterpriseClient>(createClient);
    }

    public async Task<ChartStringValidationResult> ValidateChartStringAsync(
        string chartString, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(chartString))
        {
            return new ChartStringValidationResult
            {
                ChartString = chartString,
                Messages = ["A chart string is required."],
            };
        }

        var type = FinancialChartValidation.GetFinancialChartStringType(chartString);
        if (type == FinancialChartStringType.Invalid)
        {
            return new ChartStringValidationResult
            {
                ChartString = chartString,
                Messages = ["The chart string must use a recognized GL or PPM format."],
            };
        }

        var messages = new List<string>();
        var warnings = new List<string>();
        bool? valid = null;
        try
        {
            if (type == FinancialChartStringType.Gl)
            {
                var result = await _client.Value.GlValidateChartstring.ExecuteAsync(chartString, false, cancellationToken);
                messages.AddRange(result.Errors.Select(error =>
                    error.Exception == null && !string.IsNullOrWhiteSpace(error.Message) ? error.Message : ServiceFailureMessage));
                var validation = result.Data?.GlValidateChartstring;
                if (validation != null)
                {
                    valid = validation.ValidationResponse.Valid;
                    messages.AddRange((validation.ValidationResponse.ErrorMessages ?? []).Select(message =>
                        string.IsNullOrWhiteSpace(message) ? "Aggie Enterprise reported a chart string validation error." : message));
                    foreach (var warning in validation.Warnings ?? [])
                    {
                        AddWarning(warnings, warning?.SegmentName, warning?.Warning);
                    }
                }
            }
            else
            {
                var result = await _client.Value.PpmSegmentStringValidate.ExecuteAsync(chartString, cancellationToken);
                messages.AddRange(result.Errors.Select(error =>
                    error.Exception == null && !string.IsNullOrWhiteSpace(error.Message) ? error.Message : ServiceFailureMessage));
                var validation = result.Data?.PpmSegmentStringValidate;
                if (validation != null)
                {
                    valid = validation.ValidationResponse.Valid;
                    messages.AddRange((validation.ValidationResponse.ErrorMessages ?? []).Select(message =>
                        string.IsNullOrWhiteSpace(message) ? "Aggie Enterprise reported a chart string validation error." : message));
                    foreach (var warning in validation.Warnings ?? [])
                    {
                        AddWarning(warnings, warning?.SegmentName, warning?.Warning);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The SDK can include OAuth responses in exceptions. Return a safe message without those details.
            messages.Add(ServiceFailureMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (valid != true && messages.Count == 0)
        {
            messages.Add(valid == false
                ? "The chart string is not valid."
                : "Aggie Enterprise did not return a chart string validation result.");
        }

        return new ChartStringValidationResult
        {
            ChartString = chartString,
            IsValid = valid == true && messages.Count == 0,
            ChartType = type == FinancialChartStringType.Gl ? "GL" : "PPM",
            Messages = messages.ToArray(),
            Warnings = warnings.ToArray(),
        };
    }

    private static void AddWarning(List<string> warnings, string? segmentName, string? warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
        {
            warnings.Add(string.IsNullOrWhiteSpace(segmentName) ? warning : $"{segmentName}: {warning}");
        }
    }
}

public static class AggieEnterpriseServiceCollectionExtensions
{
    public static IServiceCollection AddAggieEnterpriseService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAggieEnterpriseClient>(_ =>
        {
            var settings = configuration.GetSection("AggieEnterprise");
            var graphQlUrl = settings["GraphQlUrl"];
            var tokenEndpoint = settings["TokenEndpoint"];
            var consumerKey = settings["ConsumerKey"];
            var consumerSecret = settings["ConsumerSecret"];
            var scopeApp = settings["ScopeApp"];
            var scopeEnv = settings["ScopeEnv"];

            if (!Uri.TryCreate(graphQlUrl, UriKind.Absolute, out var graphQlUri) || graphQlUri.Scheme != Uri.UriSchemeHttps ||
                !Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out var tokenUri) || tokenUri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret) ||
                string.IsNullOrWhiteSpace(scopeApp) || string.IsNullOrWhiteSpace(scopeEnv))
            {
                throw new InvalidOperationException("Configure AggieEnterprise:GraphQlUrl, ConsumerKey, ConsumerSecret, TokenEndpoint, ScopeApp, and ScopeEnv before validating chart strings. Both endpoints must use HTTPS.");
            }

            return GraphQlClient.Get(graphQlUrl, tokenEndpoint, consumerKey, consumerSecret, $"{scopeApp}-{scopeEnv}");
        });
        // Defer client creation so unrelated application features work without Aggie Enterprise configuration.
        services.AddSingleton<IAggieEnterpriseService>(provider =>
            new AggieEnterpriseService(() => provider.GetRequiredService<IAggieEnterpriseClient>()));
        return services;
    }
}

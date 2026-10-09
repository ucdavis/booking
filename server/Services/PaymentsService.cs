using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Server.Models.Payments;

namespace Server.Services;

public interface IPaymentsService
{
    Task<PaymentsTeam?> GetTeamForApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
    Task<PaymentsTeam?> GetTeamAsync(string secretName, CancellationToken cancellationToken = default);
}

// Callers supply the team's vault reference explicitly, so background work needs no HTTP context or user session.
public sealed class PaymentsService(
    IHttpClientFactory httpClientFactory, ISecretsService secretsService, IOptions<PaymentsOptions> options) : IPaymentsService
{
    public const string ClientName = "Payments";

    public async Task<PaymentsTeam?> GetTeamAsync(string secretName, CancellationToken cancellationToken = default)
    {
        var apiKey = await secretsService.GetSecretAsync(secretName, cancellationToken);
        return await GetTeamForApiKeyAsync(apiKey, cancellationToken);
    }

    public async Task<PaymentsTeam?> GetTeamForApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096 || apiKey.Any(char.IsControl))
        {
            return null;
        }

        var baseUrl = options.Value.BaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("Configure Payments:BaseUrl with the HTTPS payments service URL.");
        }

        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/api/team/"));
            // Payments expects the raw key, without a Bearer prefix. Never put team credentials on shared defaults.
            request.Headers.TryAddWithoutValidation("Authorization", apiKey);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Payments could not verify the API key. Try again later.");
            }

            var team = await response.Content.ReadFromJsonAsync<PaymentsTeam>(cancellationToken);
            if (team == null || string.IsNullOrWhiteSpace(team.Name) || string.IsNullOrWhiteSpace(team.Slug) ||
                team.Name.Length > 200 || team.Name.Any(char.IsControl) ||
                team.Slug.Trim().Length > 100 || team.Slug.Any(char.IsControl))
            {
                throw new HttpRequestException("Payments returned incomplete team information. Try again later.");
            }

            return new PaymentsTeam { Name = team.Name.Trim(), Slug = team.Slug.Trim() };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The Payments connection timed out. Try again later.");
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is JsonException ||
            exception is NotSupportedException || exception is FormatException)
        {
            // Upstream bodies and transport exceptions may include credentials; do not retain them as inner exceptions.
            throw new HttpRequestException("Payments could not verify the API key. Try again later.");
        }
    }
}

public static class PaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentsService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentsOptions>().Bind(configuration.GetSection(PaymentsOptions.SectionName));
        services.AddHttpClient(PaymentsService.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 64 * 1024;
        })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RedactLoggedHeaders(_ => true);
        services.AddSingleton<IPaymentsService, PaymentsService>();
        return services;
    }
}

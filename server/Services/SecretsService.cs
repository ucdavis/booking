using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Server.Services;

public interface ISecretsService
{
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string secretName, string value, CancellationToken cancellationToken = default);
}

public sealed class SecretsService(SecretClient client) : ISecretsService
{
    public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        var secret = await client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return secret.Value.Value;
    }

    public async Task SetSecretAsync(string secretName, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentException.ThrowIfNullOrEmpty(value);

        await client.SetSecretAsync(secretName, value, cancellationToken);
    }
}

public static class SecretsServiceCollectionExtensions
{
    public static IServiceCollection AddSecretsService(this IServiceCollection services, IConfiguration configuration)
    {
        // Defer configuration validation and client creation until secrets are needed.
        services.AddSingleton<SecretClient>(_ =>
        {
            var vaultUrl = configuration["Azure:KeyVaultUrl"];
            if (!Uri.TryCreate(vaultUrl, UriKind.Absolute, out var vaultUri) || vaultUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Configure Azure:KeyVaultUrl with an HTTPS Key Vault URL before using the secrets service.");
            }

            var tenantId = configuration["Azure:TenantId"];
            var clientId = configuration["Azure:ClientId"];
            var clientSecret = configuration["Azure:ClientSecret"];

            TokenCredential credential;
            if (tenantId != null || clientId != null || clientSecret != null)
            {
                if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) ||
                    string.IsNullOrWhiteSpace(clientSecret))
                {
                    throw new InvalidOperationException("Configure Azure:TenantId, Azure:ClientId, and Azure:ClientSecret together, or omit all three to use DefaultAzureCredential.");
                }

                credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }
            else
            {
                credential = new DefaultAzureCredential();
            }

            return new SecretClient(vaultUri, credential);
        });
        services.AddSingleton<ISecretsService, SecretsService>();
        return services;
    }
}

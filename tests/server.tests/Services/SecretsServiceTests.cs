using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Core.Domain;
using Server.Services;

namespace Server.Tests.Services;

public class SecretsServiceTests
{
    private const string VaultUrl = "https://test-vault.vault.azure.net/";
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string ClientId = "22222222-2222-2222-2222-222222222222";
    private const string ClientSecret = "synthetic-client-secret-for-tests";

    [Fact]
    public void Registration_allows_startup_without_resolving_vault_configuration_or_credentials()
    {
        var services = new ServiceCollection();
        services.AddSecretsService(new ConfigurationBuilder().Build());

        using var provider = BuildProvider(services);
        provider.GetRequiredService<ISecretsService>().Should().BeOfType<SecretsService>();
    }

    [Fact]
    public async Task Registered_service_reads_through_an_overridden_secret_client()
    {
        var client = new TestSecretClient();
        var services = new ServiceCollection();
        services.AddSecretsService(new ConfigurationBuilder().Build());
        services.AddSingleton<SecretClient>(client);
        using var provider = BuildProvider(services);

        var service = provider.GetRequiredService<ISecretsService>();
        var value = await service.GetSecretAsync("test-team-payments-api-key");

        value.Should().Be(client.SecretValue);
        client.ReadCalls.Should().ContainSingle().Which.Name.Should().Be("test-team-payments-api-key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("http://test-vault.vault.azure.net/")]
    [InlineData("test-invalid-vault-url")]
    [InlineData("https://[test-invalid-vault-url")]
    public async Task Invalid_vault_urls_are_rejected_on_use_without_exposing_the_value(string? vaultUrl)
    {
        var services = new ServiceCollection();
        services.AddSecretsService(Configuration(new Dictionary<string, string?>
        {
            ["Azure:KeyVaultUrl"] = vaultUrl,
        }));
        using var provider = BuildProvider(services);
        var service = provider.GetRequiredService<ISecretsService>();
        var read = () => service.GetSecretAsync("test-team-payments-api-key");

        var error = (await read.Should().ThrowAsync<InvalidOperationException>()).Which;

        error.Message.Should().Contain("Azure:KeyVaultUrl");
        error.InnerException.Should().BeNull();
        if (!string.IsNullOrWhiteSpace(vaultUrl))
        {
            error.ToString().Should().NotContain(vaultUrl);
        }
    }

    [Theory]
    [InlineData("Azure:TenantId", null)]
    [InlineData("Azure:TenantId", "")]
    [InlineData("Azure:TenantId", " \t")]
    [InlineData("Azure:ClientId", null)]
    [InlineData("Azure:ClientId", "")]
    [InlineData("Azure:ClientId", " \t")]
    [InlineData("Azure:ClientSecret", null)]
    [InlineData("Azure:ClientSecret", "")]
    [InlineData("Azure:ClientSecret", " \t")]
    public void Incomplete_client_credentials_are_rejected_without_exposing_configuration_values(
        string incompleteKey, string? incompleteValue)
    {
        var values = ClientCredentialValues();
        values[incompleteKey] = incompleteValue;
        var services = new ServiceCollection();
        services.AddSecretsService(Configuration(values));
        using var provider = BuildProvider(services);
        var resolve = () => provider.GetRequiredService<SecretClient>();

        var error = (resolve.Should().Throw<InvalidOperationException>()).Which;

        error.Message.Should().Contain("Azure:TenantId").And.Contain("Azure:ClientId")
            .And.Contain("Azure:ClientSecret");
        error.ToString().Should().NotContain(ClientSecret).And.NotContain(TenantId)
            .And.NotContain(ClientId);
        error.InnerException.Should().BeNull();
    }

    [Theory]
    [InlineData("Azure:TenantId", "")]
    [InlineData("Azure:TenantId", " \t")]
    [InlineData("Azure:ClientId", "")]
    [InlineData("Azure:ClientId", " \t")]
    [InlineData("Azure:ClientSecret", "")]
    [InlineData("Azure:ClientSecret", " \t")]
    public void Explicit_blank_credentials_are_rejected_instead_of_using_default_credentials(
        string credentialKey, string credentialValue)
    {
        var services = new ServiceCollection();
        services.AddSecretsService(Configuration(new Dictionary<string, string?>
        {
            ["Azure:KeyVaultUrl"] = VaultUrl,
            [credentialKey] = credentialValue,
        }));
        using var provider = BuildProvider(services);
        var resolve = () => provider.GetRequiredService<SecretClient>();

        resolve.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(credentialKey);
    }

    [Fact]
    public void Complete_client_credentials_resolve_a_singleton_client_for_the_configured_vault()
    {
        var services = new ServiceCollection();
        services.AddSecretsService(Configuration(ClientCredentialValues()));
        using var provider = BuildProvider(services);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var client = firstScope.ServiceProvider.GetRequiredService<SecretClient>();
        var otherClient = secondScope.ServiceProvider.GetRequiredService<SecretClient>();

        client.VaultUri.Should().Be(new Uri(VaultUrl));
        otherClient.Should().BeSameAs(client);
        firstScope.ServiceProvider.GetRequiredService<ISecretsService>().Should().BeOfType<SecretsService>();
    }

    [Fact]
    public async Task Read_resolves_the_latest_value_using_the_team_secret_name()
    {
        var team = new Team
        {
            Name = "Test Team", Slug = "test-team", PaymentsApiKeySecretName = "test-team-payments-api-key",
        };
        using var cancellation = new CancellationTokenSource();
        var client = new TestSecretClient();
        ISecretsService service = new SecretsService(client);

        var value = await service.GetSecretAsync(team.PaymentsApiKeySecretName, cancellation.Token);

        value.Should().Be(client.SecretValue);
        var request = client.ReadCalls.Should().ContainSingle().Subject;
        request.Name.Should().Be(team.PaymentsApiKeySecretName);
        request.Version.Should().BeNull();
        request.Token.Should().Be(cancellation.Token);
        client.WriteCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(" test-api-key \n")]
    [InlineData(" ")]
    public async Task Write_preserves_the_exact_value_and_forwards_cancellation(string value)
    {
        using var cancellation = new CancellationTokenSource();
        var client = new TestSecretClient();
        ISecretsService service = new SecretsService(client);

        await service.SetSecretAsync("test-team-payments-api-key", value, cancellation.Token);

        var request = client.WriteCalls.Should().ContainSingle().Subject;
        request.Name.Should().Be("test-team-payments-api-key");
        request.Value.Should().Be(value);
        request.Token.Should().Be(cancellation.Token);
        client.ReadCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Missing_or_blank_names_are_rejected_before_accessing_the_vault(string? secretName)
    {
        var client = new TestSecretClient();
        var service = new SecretsService(client);

        var read = () => service.GetSecretAsync(secretName!);
        var write = () => service.SetSecretAsync(secretName!, "test-api-key");

        (await read.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("secretName");
        (await write.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("secretName");
        client.ReadCalls.Should().BeEmpty();
        client.WriteCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Missing_or_empty_values_are_rejected_before_writing(string? value)
    {
        var client = new TestSecretClient();
        var service = new SecretsService(client);

        var write = () => service.SetSecretAsync("test-team-payments-api-key", value!);

        (await write.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("value");
        client.WriteCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(404, false)]
    [InlineData(403, false)]
    [InlineData(403, true)]
    public async Task Vault_failures_propagate_instead_of_becoming_successful_results(int status, bool write)
    {
        var failure = new RequestFailedException(status, "Test vault request failed.");
        var client = new TestSecretClient { Failure = failure };
        var service = new SecretsService(client);
        Func<Task> operation = write
            ? () => service.SetSecretAsync("test-team-payments-api-key", "test-api-key")
            : () => service.GetSecretAsync("test-team-payments-api-key");

        var error = (await operation.Should().ThrowAsync<RequestFailedException>()).Which;

        error.Should().BeSameAs(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_propagates_for_reads_and_writes(bool write)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new TestSecretClient();
        var service = new SecretsService(client);
        Func<Task> operation = write
            ? () => service.SetSecretAsync("test-team-payments-api-key", "test-api-key", cancellation.Token)
            : () => service.GetSecretAsync("test-team-payments-api-key", cancellation.Token);

        var error = (await operation.Should().ThrowAsync<OperationCanceledException>()).Which;

        error.CancellationToken.Should().Be(cancellation.Token);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Dictionary<string, string?> ClientCredentialValues() => new()
    {
        ["Azure:KeyVaultUrl"] = VaultUrl,
        ["Azure:TenantId"] = TenantId,
        ["Azure:ClientId"] = ClientId,
        ["Azure:ClientSecret"] = ClientSecret,
    };

    private static ServiceProvider BuildProvider(IServiceCollection services)
    {
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true,
        });
    }

    private sealed class TestSecretClient : SecretClient
    {
        public string SecretValue { get; } = "test-api-key";
        public Exception? Failure { get; init; }
        public List<(string Name, string? Version, CancellationToken Token)> ReadCalls { get; } = [];
        public List<(string Name, string Value, CancellationToken Token)> WriteCalls { get; } = [];

        public override Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name, string? version = null, CancellationToken cancellationToken = default)
        {
            ReadCalls.Add((name, version, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure != null)
            {
                return Task.FromException<Response<KeyVaultSecret>>(Failure);
            }

            return Task.FromResult(Response.FromValue(new KeyVaultSecret(name, SecretValue), null!));
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(
            string name, string value, CancellationToken cancellationToken = default)
        {
            WriteCalls.Add((name, value, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure != null)
            {
                return Task.FromException<Response<KeyVaultSecret>>(Failure);
            }

            return Task.FromResult(Response.FromValue(new KeyVaultSecret(name, value), null!));
        }
    }
}

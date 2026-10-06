using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Server.Core.Data;
using Server.Models.Directory;
using UCD.Rosetta.Client.Core.Configuration;
using UCD.Rosetta.Client.Generated;

namespace Server.Services;

public interface IRosettaService
{
    Task<IReadOnlyList<DirectoryPerson>> SearchPeopleAsync(string search, CancellationToken cancellationToken = default);
    Task<DirectoryPerson?> FindByIamIdAsync(string iamId, CancellationToken cancellationToken = default);
}

public sealed class RosettaService(
    AppDbContext dbContext, IHttpClientFactory httpClientFactory, IOptions<RosettaClientOptions> options) : IRosettaService
{
    private static readonly Regex EmailPattern = new(
        @"\A[A-Za-z0-9._%+-]+@(?:[A-Za-z0-9-]+\.)*[uU][cC][dD][aA][vV][iI][sS]\.[eE][dD][uU]\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<IReadOnlyList<DirectoryPerson>> SearchPeopleAsync(
        string search, CancellationToken cancellationToken = default)
    {
        search = search.Trim();
        if (search.Length == 0)
        {
            return [];
        }

        var users = await dbContext.Users.AsNoTracking()
            .Where(user => user.IamId == search || user.Email == search || user.Kerberos == search)
            .Where(user => !string.IsNullOrWhiteSpace(user.IamId) && !string.IsNullOrWhiteSpace(user.Kerberos) &&
                !string.IsNullOrWhiteSpace(user.Email))
            .OrderBy(user => user.Name).ThenBy(user => user.IamId)
            .Select(user => new DirectoryPerson
            {
                IamId = user.IamId, Name = user.Name, Email = user.Email, Kerberos = user.Kerberos,
            })
            .Take(10).ToListAsync(cancellationToken);
        if (users.Count > 0)
        {
            return users;
        }

        var isEmail = search.Contains('@');
        var isIamId = IsIamId(search);
        // Apply Rosetta's identifier syntax only after checking existing application accounts.
        if (search.Length > 128 || (isEmail && !EmailPattern.IsMatch(search)) ||
            (!isEmail && !isIamId && !IsKerberos(search)))
        {
            return [];
        }

        var people = await LookupAsync(
            iamId: isIamId ? search : null,
            email: isEmail ? search : null,
            kerberos: !isEmail && !isIamId ? search : null,
            limit: 10, cancellationToken);

        // Never expand an exact search into partial matches, even if the upstream API does.
        return people.Where(person => Matches(person, search))
            .Select(MapPerson).OfType<DirectoryPerson>()
            .DistinctBy(person => person.IamId)
            .OrderBy(person => person.Name).ThenBy(person => person.IamId)
            .Take(10).ToList();
    }

    public async Task<DirectoryPerson?> FindByIamIdAsync(string iamId, CancellationToken cancellationToken = default)
    {
        iamId = iamId.Trim();
        if (!IsIamId(iamId))
        {
            return null;
        }

        var people = await LookupAsync(iamId, email: null, kerberos: null, limit: 2, cancellationToken);
        var matches = people.Select(MapPerson).OfType<DirectoryPerson>()
            .Where(person => person.IamId == iamId).ToList();
        if (matches.Count > 1)
        {
            throw new InvalidOperationException("Rosetta returned an ambiguous IAM ID lookup.");
        }

        return matches.SingleOrDefault();
    }

    private async Task<ICollection<Person>> LookupAsync(
        string? iamId, string? email, string? kerberos, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Defer validation/client creation so existing-user searches and local sign-in work offline.
            var settings = options.Value;
            settings.Validate();
            using var httpClient = httpClientFactory.CreateClient("RosettaClient");
            var client = new Client(httpClient)
            {
                BaseUrl = settings.BaseUrl.Replace("{version}", settings.ApiVersion),
            };
            return await client.PeopleGETAsync(iamid: iamId, email: email, loginid: kerberos,
                limit: limit, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("The Rosetta directory lookup timed out.");
        }
        catch (Exception exception) when (exception is RosettaApiException || exception is HttpRequestException ||
            exception is InvalidOperationException || exception is UriFormatException || exception is JsonException)
        {
            // The client includes response bodies in exceptions, including OAuth errors. Do not retain them.
            throw new HttpRequestException("Rosetta directory lookup failed. Check the RosettaClient configuration and service availability.");
        }
    }

    private static bool Matches(Person person, string search)
        => string.Equals(Clean(person.Iam_id) ?? Clean(person.Id?.Iam_id), search, StringComparison.Ordinal) ||
            string.Equals(Clean(person.Id?.Login_id), search, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Clean(person.Email?.Campus), search, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Clean(person.Email?.Health), search, StringComparison.OrdinalIgnoreCase);

    private static bool IsIamId(string value)
        => value.Length == 10 && value.All(char.IsAsciiDigit);

    private static bool IsKerberos(string value)
        => value.Length >= 1 && value.Length <= 64 && char.IsAsciiLetterOrDigit(value[0]) && value[^1] != '.' &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_' || character == '-' || character == '.');

    private static DirectoryPerson? MapPerson(Person person)
    {
        var iamId = Clean(person.Iam_id) ?? Clean(person.Id?.Iam_id);
        var kerberos = Clean(person.Id?.Login_id);
        var campusEmail = Clean(person.Email?.Campus);
        if (iamId == null || kerberos == null || campusEmail == null)
        {
            return null;
        }

        var livedName = Clean($"{Clean(person.Name?.Lived_first_name)} {Clean(person.Name?.Lived_last_name)}");
        return new DirectoryPerson
        {
            IamId = iamId,
            Name = Clean(person.Displayname) ?? livedName ?? iamId,
            Email = campusEmail,
            Kerberos = kerberos,
        };
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Contains("*******", StringComparison.Ordinal) ? null : value.Trim();
}

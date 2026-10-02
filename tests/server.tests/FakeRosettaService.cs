using Microsoft.EntityFrameworkCore;
using Server.Core.Data;
using Server.Models.Directory;
using Server.Services;

namespace Server.Tests;

internal sealed class FakeRosettaService(AppDbContext? dbContext = null) : IRosettaService
{
    public List<DirectoryPerson> People { get; } = [];
    public List<string> SearchCalls { get; } = [];
    public List<string> LookupCalls { get; } = [];
    public Exception? Failure { get; set; }

    public async Task<IReadOnlyList<DirectoryPerson>> SearchPeopleAsync(
        string search, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SearchCalls.Add(search);
        if (dbContext != null)
        {
            var users = await dbContext.Users.AsNoTracking()
                .Where(user => user.IamId == search || user.Email == search)
                .OrderBy(user => user.Name).ThenBy(user => user.IamId)
                .Select(user => new DirectoryPerson { IamId = user.IamId, Name = user.Name, Email = user.Email })
                .Take(10).ToListAsync(cancellationToken);
            if (users.Count > 0)
            {
                return users;
            }
        }

        if (Failure != null)
        {
            throw Failure;
        }

        return People.Where(person => person.IamId == search || person.Email == search || person.Kerberos == search)
            .OrderBy(person => person.Name).ThenBy(person => person.IamId).Take(10).ToList();
    }

    public Task<DirectoryPerson?> FindByIamIdAsync(string iamId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LookupCalls.Add(iamId);
        if (Failure != null)
        {
            throw Failure;
        }

        return Task.FromResult(People.SingleOrDefault(person => person.IamId == iamId));
    }
}

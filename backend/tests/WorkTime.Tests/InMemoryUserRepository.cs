// TEST-ONLY fake (not shipped in production): in-memory implementation of IUserRepository.
// Storage is a plain dictionary keyed by Guid Id; every method is synchronous-to-completion.
using WorkTime.Domain;

public class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    // The Ids to load are taken from the keys of "ids" (the Guid values in that map are not used).
    // A requested Id that is not stored is simply absent from the result.
    public Task<Dictionary<Guid, User>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, User>();

        foreach (var id in ids.Keys)
        {
            _users.TryGetValue(id, out var user);
            if (user != null)
            {
                result[user.Id] = user;
            }
        }

        return Task.FromResult(result);
    }

    // Active users whose Login or FullName contains "name" (case-insensitive), keyed by Login.
    public Task<Dictionary<string, User>> GetActiveByNameAsync(string name, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, User>();
        var needle = name.ToLower();

        foreach (var entry in _users)
        {
            var user = entry.Value;
            if (!user.IsActive)
            {
                continue;
            }

            if (user.Login.ToLower().Contains(needle) || user.FullName.ToLower().Contains(needle))
            {
                result[user.Login] = user;
            }
        }

        return Task.FromResult(result);
    }

    // Exact login match (case-insensitive), not a substring search. Deactivated
    // users are returned as well, so AuthService can report a blocked or locked
    // account instead of "no such login" (BR24, BR35).
    public Task<User?> GetByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var needle = login.ToLower();

        foreach (var entry in _users)
        {
            if (entry.Value.Login.ToLower() == needle)
            {
                return Task.FromResult<User?>(entry.Value);
            }
        }

        return Task.FromResult((User?)null);
    }

    // BR16 rows: active users of one department. Guid.Empty means "every active user of
    // the system", so a service never has to be handed the user ids it wants.
    public async Task<IEnumerable<User>> GetActiveEmployeesInDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var result = new List<User>();

        foreach (var entry in _users)
        {
            var user = entry.Value;
            if (!user.IsActive)
            {
                continue;
            }

            if (departmentId != Guid.Empty && user.DepartmentId != departmentId)
            {
                continue;
            }

            result.Add(user);
        }

        return result;
    }

    // Every account, deactivated included (mirrors the production repository).
    public Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<User>>(new List<User>(_users.Values));
    }

    public Task SaveAsync(User user, CancellationToken cancellationToken)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _users.Remove(id);
        return Task.CompletedTask;
    }
}

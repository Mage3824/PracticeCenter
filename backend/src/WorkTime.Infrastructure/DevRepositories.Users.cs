// Dev-tier ("in-memory") repositories for the WorkTime.Api (Stage 2).
//
// These are NOT production repositories: they keep state in process memory so that the Web
// API can run end-to-end without a database. The production, database-backed implementations
// replace them through the same constructor injection (see InfrastructureLayer.cs).
//
// They deliberately live in namespace WorkTime.Infrastructure: tests/WorkTime.Tests declares
// identically named InMemory*Repository fakes in the global namespace, and an unqualified
// duplicate here would make those names ambiguous for every project that sees both.
using WorkTime.Domain;

namespace WorkTime.Infrastructure;

// In-memory implementation of IUserRepository. Storage is a plain dictionary keyed by Guid Id.
public class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
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
    public Task<Dictionary<String, User>> GetActiveByNameAsync(String name, CancellationToken cancellationToken)
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

    // Exact login match (case-insensitive). Deactivated users are returned as well, so AuthService
    // can report a blocked / locked account instead of "no such login" (BR24, BR35).
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

    // BR16 rows: active users of one department. Guid.Empty means "every active user of the system".
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

    // Every account, deactivated included (BR24, BR20 guard for the last active administrator).
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

// In-memory implementation of IDepartmentRepository. The hierarchy is walked through ParentId.
public class InMemoryDepartmentRepository : IDepartmentRepository
{
    private readonly Dictionary<Guid, Department> _departments = new();

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _departments.TryGetValue(id, out var department);
        return Task.FromResult(department);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
    public Task<Dictionary<Guid, Department>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Department>();

        foreach (var id in ids.Keys)
        {
            _departments.TryGetValue(id, out var department);
            if (department != null)
            {
                result[department.Id] = department;
            }
        }

        return Task.FromResult(result);
    }

    // Departments directly under the given parent (the hierarchy walk for BR16/BR19 grouping).
    public async Task<IEnumerable<Department>> GetChildrenOfAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var result = new List<Department>();

        foreach (var entry in _departments)
        {
            var department = entry.Value;
            if (department.ParentId != null && department.ParentId == parentId)
            {
                result.Add(department);
            }
        }

        return result;
    }

    // Every department, top level included.
    public Task<IEnumerable<Department>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<Department>>(new List<Department>(_departments.Values));
    }

    public Task SaveAsync(Department department, CancellationToken cancellationToken)
    {
        _departments[department.Id] = department;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _departments.Remove(id);
        return Task.CompletedTask;
    }
}

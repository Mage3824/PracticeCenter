// TEST-ONLY fake (not shipped in production): in-memory implementation of IDepartmentRepository.
// Storage is a plain dictionary keyed by Guid Id; the hierarchy is walked through ParentId.
using WorkTime.Domain;

public class InMemoryDepartmentRepository : IDepartmentRepository
{
    private readonly Dictionary<Guid, Department> _departments = new();

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _departments.TryGetValue(id, out var department);
        return Task.FromResult(department);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
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

    // Departments directly under the given parent (BR16/BR19 grouping walk).
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

    // Every department, top level included (mirrors the production repository).
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

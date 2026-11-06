// TEST-ONLY fake (not shipped in production): in-memory implementation of IPeriodClosureRepository.
// PeriodClosure now carries DepartmentId (see src/WorkTime.Domain/Entities.cs), so "the active
// closure of a department" is answered honestly instead of ignoring the requested department.
using WorkTime.Domain;

public class InMemoryPeriodClosureRepository : IPeriodClosureRepository
{
    private readonly Dictionary<Guid, PeriodClosure> _closures = new();

    public Task<PeriodClosure?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _closures.TryGetValue(id, out var closure);
        return Task.FromResult(closure);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
    public Task<Dictionary<Guid, PeriodClosure>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PeriodClosure>();

        foreach (var id in ids.Keys)
        {
            _closures.TryGetValue(id, out var closure);
            if (closure != null)
            {
                result[closure.Id] = closure;
            }
        }

        return Task.FromResult(result);
    }

    public Task<PeriodClosure?> GetActiveForDepartmentAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var active = TryFindActiveClosure(departmentId);
        return Task.FromResult(active);
    }

    // Every closure of one department, open and closed (BR23.2 needs the closed one to reopen).
    public Task<Dictionary<Guid, PeriodClosure>> GetAllForDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PeriodClosure>();

        foreach (var entry in _closures)
        {
            if (departmentId == Guid.Empty || entry.Value.DepartmentId == departmentId)
            {
                result[entry.Key] = entry.Value;
            }
        }

        return Task.FromResult(result);
    }

    // The closure of one department standing for one calendar month (BR18 custom period).
    public Task<PeriodClosure?> GetForDepartmentOnMonthAsync(
        Guid departmentId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        foreach (var entry in _closures)
        {
            var closure = entry.Value;
            if (closure.Year == year && closure.Month == month
                && (departmentId == Guid.Empty || closure.DepartmentId == departmentId))
            {
                return Task.FromResult<PeriodClosure?>(closure);
            }
        }

        return Task.FromResult((PeriodClosure?)null);
    }

    // Oldest-first comparison by Year, then Month; closed closures are skipped. Guid.Empty
    // means "any department". A period reopened by the administrator (BR23.2) is not closed
    // any more, so it becomes active again and its ReopenReason stays visible for the
    // "Emergency correction" record.
    private PeriodClosure? TryFindActiveClosure(Guid departmentId)
    {
        PeriodClosure? latest = null;

        foreach (var entry in _closures)
        {
            var closure = entry.Value;
            if (closure.IsClosed)
            {
                continue;
            }

            if (departmentId != Guid.Empty && closure.DepartmentId != departmentId)
            {
                continue;
            }

            if (latest == null)
            {
                latest = closure;
                continue;
            }

            if (closure.Year > latest.Year || (closure.Year == latest.Year && closure.Month > latest.Month))
            {
                latest = closure;
            }
        }

        return latest;
    }

    public Task SaveAsync(PeriodClosure periodClosure, CancellationToken cancellationToken)
    {
        _closures[periodClosure.Id] = periodClosure;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _closures.Remove(id);
        return Task.CompletedTask;
    }
}

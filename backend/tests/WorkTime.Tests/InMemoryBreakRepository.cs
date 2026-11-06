// TEST-ONLY fake (not shipped in production): in-memory implementation of IBreakRepository.
// Break keeps only WorkDayId, so "employee" is resolved through an injected work-day repository
// (constructor injection, DI style).
using WorkTime.Domain;

public class InMemoryBreakRepository : IBreakRepository
{
    private readonly Dictionary<Guid, Break> _breaks = new();

    // Resolves Break.WorkDayId to the employee that owns that work day.
    private readonly IWorkDayRepository _workDays;

    public InMemoryBreakRepository(IWorkDayRepository workDays)
    {
        _workDays = workDays;
    }

    // Un-wired repository: GetUnfinishedForEmployeeAsync returns an empty dictionary until a work-day
    // repository is supplied (Stage 3 wires it as a package dependency).
    public InMemoryBreakRepository() : this(null!)
    {
    }

    public Task<Break?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _breaks.TryGetValue(id, out var workBreak);
        return Task.FromResult(workBreak);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
    public Task<Dictionary<Guid, Break>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Break>();

        foreach (var id in ids.Keys)
        {
            _breaks.TryGetValue(id, out var workBreak);
            if (workBreak != null)
            {
                result[workBreak.Id] = workBreak;
            }
        }

        return Task.FromResult(result);
    }

    // Every break of one working day, open or finished. This is the query that lets
    // BreakService/WorkTimeService/ReportService total and list a day's breaks without
    // having their ids supplied by the caller.
    public async Task<IEnumerable<Break>> GetByWorkDayIdAsync(Guid workDayId, CancellationToken cancellationToken)
    {
        var result = new List<Break>();

        foreach (var entry in _breaks)
        {
            if (entry.Value.WorkDayId == workDayId)
            {
                result.Add(entry.Value);
            }
        }

        return result;
    }

    // Breaks that have no EndTime yet and belong to a work day of this employee.
    public async Task<Dictionary<Guid, Break>> GetUnfinishedForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Break>();

        if (_workDays == null)
        {
            return result;
        }

        foreach (var entry in _breaks)
        {
            var workBreak = entry.Value;
            if (workBreak.EndTime != null)
            {
                continue;
            }

            var workDay = await _workDays.GetByIdAsync(workBreak.WorkDayId, cancellationToken);
            if (workDay != null && workDay.UserId == employeeId)
            {
                result[workBreak.Id] = workBreak;
            }
        }

        return result;
    }

    public Task SaveAsync(Break workBreak, CancellationToken cancellationToken)
    {
        _breaks[workBreak.Id] = workBreak;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _breaks.Remove(id);
        return Task.CompletedTask;
    }
}

// TEST-ONLY fake (not shipped in production): in-memory implementation of IWorkDayRepository.
// WorkDay stores only Date, so periodClosureId is resolved through an injected closure repository
// to obtain the Year/Month window (constructor injection, DI style).
using WorkTime.Domain;

public class InMemoryWorkDayRepository : IWorkDayRepository
{
    private readonly Dictionary<Guid, WorkDay> _workDays = new();

    // Supplies the calendar month (Year/Month) that a period closure stands for.
    private readonly IPeriodClosureRepository _periodClosures;

    public InMemoryWorkDayRepository(IPeriodClosureRepository periodClosures)
    {
        _periodClosures = periodClosures;
    }

    // Un-wired repository: GetActiveForEmployeeAsync returns an empty dictionary until a closure
    // repository is supplied (Stage 3 wires it as a package dependency).
    public InMemoryWorkDayRepository() : this(null!)
    {
    }

    public Task<WorkDay?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _workDays.TryGetValue(id, out var workDay);
        return Task.FromResult(workDay);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
    public Task<Dictionary<Guid, WorkDay>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, WorkDay>();

        foreach (var id in ids.Keys)
        {
            _workDays.TryGetValue(id, out var workDay);
            if (workDay != null)
            {
                result[workDay.Id] = workDay;
            }
        }

        return Task.FromResult(result);
    }

    // BR15/BR16: days that have an end time and are not Active, dated inside the
    // inclusive calendar window [startDate, endDate].
    public async Task<IEnumerable<WorkDay>> GetFinishedDaysForPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkDay>();

        foreach (var entry in _workDays)
        {
            var workDay = entry.Value;
            if (workDay.EndTime == null || workDay.Status == WorkDayStatus.Active)
            {
                continue;
            }

            if (TryWithinPeriod(workDay.Date, startDate, endDate))
            {
                result.Add(workDay);
            }
        }

        return result;
    }

    // Every day of one employee (open or finished) inside the inclusive calendar window.
    public async Task<IEnumerable<WorkDay>> GetForEmployeeInPeriodAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkDay>();

        foreach (var entry in _workDays)
        {
            var workDay = entry.Value;
            if (workDay.UserId != employeeId)
            {
                continue;
            }

            if (TryWithinPeriod(workDay.Date, startDate, endDate))
            {
                result.Add(workDay);
            }
        }

        return result;
    }

    // Work days of this employee that are still open (Status == Active) and fall inside the
    // calendar month of the given period closure. Unknown closure -> empty dictionary.
    public async Task<Dictionary<Guid, WorkDay>> GetActiveForEmployeeAsync(Guid employeeId, Guid periodClosureId, CancellationToken cancellationToken)    {
        var result = new Dictionary<Guid, WorkDay>();

        if (_periodClosures == null)
        {
            return result;
        }

        var closure = await _periodClosures.GetByIdAsync(periodClosureId, cancellationToken);
        if (closure == null)
        {
            return result;
        }

        foreach (var entry in _workDays)
        {
            var workDay = entry.Value;
            if (workDay.UserId != employeeId)
            {
                continue;
            }

            if (workDay.Status != WorkDayStatus.Active)
            {
                continue;
            }

            if (workDay.Date.Year == closure.Year && workDay.Date.Month == closure.Month)
            {
                result[workDay.Id] = workDay;
            }
        }

        return result;
    }

    // BR1: open days of this employee on one calendar date. Answered without consulting
    // any period closure, so the guard holds even in a world where no closure exists.
    public async Task<Dictionary<Guid, WorkDay>> GetActiveForEmployeeOnDateAsync(
        Guid employeeId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, WorkDay>();
        var needle = date.Date;

        foreach (var entry in _workDays)
        {
            var workDay = entry.Value;
            if (workDay.UserId != employeeId || workDay.Status != WorkDayStatus.Active)
            {
                continue;
            }

            if (workDay.Date.Date == needle)
            {
                result[workDay.Id] = workDay;
            }
        }

        return result;
    }

    public Task SaveAsync(WorkDay workDay, CancellationToken cancellationToken)
    {
        _workDays[workDay.Id] = workDay;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _workDays.Remove(id);
        return Task.CompletedTask;
    }

    // Inclusive comparison on calendar dates only, so the time part of a boundary is ignored.
    private static bool TryWithinPeriod(DateTime day, DateTime startDate, DateTime endDate)
    {
        var calendarDay = day.Date;
        return calendarDay >= startDate.Date && calendarDay <= endDate.Date;
    }
}

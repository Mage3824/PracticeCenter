// Dev-tier ("in-memory") repositories for the WorkTime.Api (Stage 2) - see DevRepositories.Users.cs
// for the layer note. Constructor injection is used to answer questions the entity itself cannot
// answer (WorkDay knows only a Date, Break knows only a WorkDayId).
using System.Collections.Concurrent;
using WorkTime.Domain;

namespace WorkTime.Infrastructure;

// In-memory implementation of IWorkDayRepository.
// The calendar month a period closure stands for comes from an injected closure repository, because
// WorkDay stores only Date (DI style, never a caller-supplied list of ids).
public class InMemoryWorkDayRepository : IWorkDayRepository
{
    // Singleton-репозиторий разделяется всеми запросами: ConcurrentDictionary даёт
    // атомарные Save/Delete и перечисление без гонки.
    private readonly ConcurrentDictionary<Guid, WorkDay> _workDays = new();

    // Supplies the Year/Month a period closure stands for.
    private readonly IPeriodClosureRepository _periodClosures;

    public InMemoryWorkDayRepository(IPeriodClosureRepository periodClosures)
    {
        _periodClosures = periodClosures;
    }

    public Task<WorkDay?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _workDays.TryGetValue(id, out var workDay);
        return Task.FromResult(workDay);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
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

    // BR15/BR16: days that have an end time and are not Active, dated inside the inclusive
    // calendar window [startDate, endDate].
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

    // Open days of this employee inside the calendar month of the given period closure.
    // Unknown closure Id -> empty dictionary.
    public async Task<Dictionary<Guid, WorkDay>> GetActiveForEmployeeAsync(
        Guid employeeId,
        Guid periodClosureId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, WorkDay>();

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

    // BR1: open days of this employee on one calendar date, answered without consulting any
    // period closure.
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
        _workDays.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    // Inclusive comparison on calendar dates only, so the time part of a boundary is ignored.
    private static bool TryWithinPeriod(DateTime day, DateTime startDate, DateTime endDate)
    {
        var calendarDay = day.Date;
        return calendarDay >= startDate.Date && calendarDay <= endDate.Date;
    }
}

// In-memory implementation of IBreakRepository. "Employee" is resolved through an injected
// work-day repository: Break.WorkDayId -> WorkDay.UserId (DI style).
public class InMemoryBreakRepository : IBreakRepository
{
    private readonly ConcurrentDictionary<Guid, Break> _breaks = new();

    // Resolves Break.WorkDayId to the employee that owns that work day.
    private readonly IWorkDayRepository _workDays;

    public InMemoryBreakRepository(IWorkDayRepository workDays)
    {
        _workDays = workDays;
    }

    public Task<Break?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _breaks.TryGetValue(id, out var workBreak);
        return Task.FromResult(workBreak);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
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

    // Every break of one working day, open or finished.
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
    public async Task<Dictionary<Guid, Break>> GetUnfinishedForEmployeeAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Break>();

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
        _breaks.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}

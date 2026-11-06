// Dev-tier ("in-memory") repositories for the WorkTime.Api (Stage 2) - see DevRepositories.Users.cs
// for the layer note.
using WorkTime.Domain;

namespace WorkTime.Infrastructure;

// In-memory implementation of IAbsenceRepository. Absence stores a Date, so periodClosureId is
// resolved through an injected closure repository to obtain its calendar month (DI style).
public class InMemoryAbsenceRepository : IAbsenceRepository
{
    private readonly Dictionary<Guid, Absence> _absences = new();

    // Supplies the Year/Month a period closure stands for.
    private readonly IPeriodClosureRepository _periodClosures;

    public InMemoryAbsenceRepository(IPeriodClosureRepository periodClosures)
    {
        _periodClosures = periodClosures;
    }

    public Task<Absence?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _absences.TryGetValue(id, out var absence);
        return Task.FromResult(absence);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
    public Task<Dictionary<Guid, Absence>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Absence>();

        foreach (var id in ids.Keys)
        {
            _absences.TryGetValue(id, out var absence);
            if (absence != null)
            {
                result[absence.Id] = absence;
            }
        }

        return Task.FromResult(result);
    }

    // Absences dated inside the calendar month of the given period closure. Unknown closure Id
    // -> empty dictionary.
    public async Task<Dictionary<Guid, Absence>> GetForPeriodClosureAsync(
        Guid periodClosureId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Absence>();

        var closure = await _periodClosures.GetByIdAsync(periodClosureId, cancellationToken);
        if (closure == null)
        {
            return result;
        }

        foreach (var entry in _absences)
        {
            var absence = entry.Value;
            if (absence.Date.Year == closure.Year && absence.Date.Month == closure.Month)
            {
                result[absence.Id] = absence;
            }
        }

        return result;
    }

    // BR26: the absences of a report window, whatever the closure behind it is.
    public async Task<IEnumerable<Absence>> GetByDateRangeAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var result = new List<Absence>();

        foreach (var entry in _absences)
        {
            if (TryWithinPeriod(entry.Value.Date, startDate, endDate))
            {
                result.Add(entry.Value);
            }
        }

        return result;
    }

    // BR27: the absence status of one employee on one calendar date.
    public async Task<IEnumerable<Absence>> GetForEmployeeOnDateAsync(
        Guid employeeId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var result = new List<Absence>();
        var needle = date.Date;

        foreach (var entry in _absences)
        {
            var absence = entry.Value;
            if (absence.UserId == employeeId && absence.Date.Date == needle)
            {
                result.Add(absence);
            }
        }

        return result;
    }

    public Task SaveAsync(Absence absence, CancellationToken cancellationToken)
    {
        _absences[absence.Id] = absence;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _absences.Remove(id);
        return Task.CompletedTask;
    }

    // Inclusive comparison on calendar dates only, so the time part of a boundary is ignored.
    private static bool TryWithinPeriod(DateTime day, DateTime startDate, DateTime endDate)
    {
        var calendarDay = day.Date;
        return calendarDay >= startDate.Date && calendarDay <= endDate.Date;
    }
}

// In-memory implementation of IAuditLogRepository. Entries are addressed by their own Id and read
// back by the entity they changed (BR30).
public class InMemoryAuditLogRepository : IAuditLogRepository
{
    private readonly Dictionary<Guid, AuditLog> _auditLogs = new();

    public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _auditLogs.TryGetValue(id, out var auditLog);
        return Task.FromResult(auditLog);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
    public Task<Dictionary<Guid, AuditLog>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, AuditLog>();

        foreach (var id in ids.Keys)
        {
            _auditLogs.TryGetValue(id, out var auditLog);
            if (auditLog != null)
            {
                result[auditLog.Id] = auditLog;
            }
        }

        return Task.FromResult(result);
    }

    // BR30/BR29: the entries written about one entity.
    public async Task<IEnumerable<AuditLog>> GetHistoryForEntityAsync(
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var result = new List<AuditLog>();

        foreach (var entry in _auditLogs)
        {
            if (entry.Value.EntityId == entityId)
            {
                result.Add(entry.Value);
            }
        }

        return result;
    }

    public Task SaveAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        _auditLogs[auditLog.Id] = auditLog;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _auditLogs.Remove(id);
        return Task.CompletedTask;
    }
}

// In-memory implementation of IPeriodClosureRepository. PeriodClosure carries DepartmentId, so
// "the active closure of a department" is answered honestly.
public class InMemoryPeriodClosureRepository : IPeriodClosureRepository
{
    private readonly Dictionary<Guid, PeriodClosure> _closures = new();

    public Task<PeriodClosure?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _closures.TryGetValue(id, out var closure);
        return Task.FromResult(closure);
    }

    // Requested Ids are taken from the keys of "ids"; a missing Id is simply absent from the result.
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

    // Newest-openest comparison by Year, then Month; closed closures are skipped. Guid.Empty means
    // "any department". A closure reopened by the administrator (BR23.2) is not closed any more.
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

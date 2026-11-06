// TEST-ONLY fake (not shipped in production): in-memory implementation of IAbsenceRepository.
// Absence stores a Date, so periodClosureId is resolved through an injected closure repository
// to obtain its calendar month (constructor injection, DI style). The date-range queries need no
// such indirection and answer BR26/BR27 directly.
using WorkTime.Domain;

public class InMemoryAbsenceRepository : IAbsenceRepository
{
    private readonly Dictionary<Guid, Absence> _absences = new();

    // Supplies the Year/Month a period closure stands for.
    private readonly IPeriodClosureRepository _periodClosures;

    public InMemoryAbsenceRepository(IPeriodClosureRepository periodClosures)
    {
        _periodClosures = periodClosures;
    }

    // Un-wired repository: GetForPeriodClosureAsync returns an empty dictionary until a closure
    // repository is supplied (Stage 3 wires it as a package dependency).
    public InMemoryAbsenceRepository() : this(null!)
    {
    }

    public Task<Absence?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _absences.TryGetValue(id, out var absence);
        return Task.FromResult(absence);
    }

    // Requested Ids are taken from the keys of "ids"; missing Ids are absent from the result.
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

    // Absences dated inside the calendar month of the given period closure.
    // Unknown closure Id -> empty dictionary.
    public async Task<Dictionary<Guid, Absence>> GetForPeriodClosureAsync(Guid periodClosureId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Absence>();

        if (_periodClosures == null)
        {
            return result;
        }

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
    public async Task<IEnumerable<Absence>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
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
    public async Task<IEnumerable<Absence>> GetForEmployeeOnDateAsync(Guid employeeId, DateTime date, CancellationToken cancellationToken)
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

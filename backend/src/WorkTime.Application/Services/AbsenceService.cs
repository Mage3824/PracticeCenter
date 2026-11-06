// Service that sets and reads the "Absence" status of an employee for a calendar
// day. Rules covered: BR25 (only the Administrator and the Manager may set/change
// the status, with a reason), BR27 (an employee cannot start a working day on a
// date that carries an absence status - CanStartWorkDayAsync reports it as a plain
// boolean guard and never throws; WorkTimeService.StartWorkDayAsync turns the same
// fact into a BusinessRuleException), BR28 (employees cannot change the status).
using WorkTime.Domain;

public class AbsenceService
{
    private readonly IUserRepository _users;

    private readonly IAbsenceRepository _absences;

    // Supplies the calendar month (Year/Month) a period closure stands for, so an
    // absence of a date can be looked up through the employee's department.
    private readonly IPeriodClosureRepository _periodClosures;

    public AbsenceService(IUserRepository users, IAbsenceRepository absences, IPeriodClosureRepository periodClosures)
    {
        _users = users;
        _absences = absences;
        _periodClosures = periodClosures;
    }

    // BR25 + BR28: the administrator or manager sets/changes the absence status of
    // an employee for one calendar day, with a reason.
    public async Task<Absence> SetAbsenceAsync(
        Guid actorId,
        Guid employeeId,
        DateTime date,
        AbsenceReason reason,
        CancellationToken cancellationToken)
    {
        var actor = await _users.GetByIdAsync(actorId, cancellationToken);
        if (actor == null)
        {
            throw new EntityNotFoundException(actorId, "User");
        }

        // BR25/BR28: an employee is not allowed to set or change the status.
        if (actor.Role != UserRole.Admin && actor.Role != UserRole.Manager)
        {
            throw new BusinessRuleException(
                "BR25",
                "[RU] Only an administrator or a manager may set the absence status.");
        }

        var target = await _users.GetByIdAsync(employeeId, cancellationToken);
        if (target == null)
        {
            throw new EntityNotFoundException(employeeId, "User");
        }

        // Правка аудита 3.2/3.3: мьютекс и sync-over-async убраны, путь вызова полностью асинхронный.
        // BR28: changing an existing status reuses its Id so that history stays
        // attached to one absence record for the day.
        var existing = await TryFindAbsenceForAsync(employeeId, date, cancellationToken);
        if (existing != null)
        {
            existing.Reason = reason;

            // BR25/BR30: the actor who set the current status is recorded, so the
            // BR27 tooltip can name whom the employee has to contact.
            existing.CreatedByUserId = actorId;

            await _absences.SaveAsync(existing, cancellationToken);
            return existing;
        }

        var absence = new Absence
        {
            Id = Guid.NewGuid(),
            UserId = employeeId,
            Date = date.Date,
            Reason = reason,

            // BR25: the administrator or manager who set this status (BR30 author).
            CreatedByUserId = actorId,
        };

        await _absences.SaveAsync(absence, cancellationToken);
        return absence;
    }

    // BR27 guard: true when the employee may start a working day on this date.
    public async Task<bool> CanStartWorkDayAsync(
        Guid employeeId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var absences = await TryFindAbsenceForAsync(employeeId, date, cancellationToken);
        return absences == null;
    }

    // Resolves the absence record of this employee on this calendar day through the
    // active period closure of his department. Null when nothing blocks the start.
    private async Task<Absence?> TryFindAbsenceForAsync(
        Guid employeeId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(employeeId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        var closure = await _periodClosures.GetActiveForDepartmentAsync(user.DepartmentId, cancellationToken);
        if (closure != null)
        {
            var absences = await _absences.GetForPeriodClosureAsync(closure.Id, cancellationToken);

            foreach (var entry in absences)
            {
                var absence = entry.Value;
                if (absence.UserId == employeeId && sameCalendarDay(absence.Date, date))
                {
                    return absence;
                }
            }
        }

        // BR27 привязан к календарной дате, а не к закрытию периода: активного closure может
        // не быть (или его месяц не совпадает с датой), и отсутствие всё равно блокирует старт.
        // Тот же источник, что использует WorkTimeService.StartWorkDayAsync (BR27 на POST /start).
        // Сравнение только по дате (.Date), без времени и часовых поясов.
        foreach (var absence in await _absences.GetForEmployeeOnDateAsync(employeeId, date.Date, cancellationToken))
        {
            if (sameCalendarDay(absence.Date, date))
            {
                return absence;
            }
        }

        return null;
    }

    private static bool sameCalendarDay(DateTime left, DateTime right)
    {
        return left.Day == right.Day && left.Month == right.Month && left.Year == right.Year;
    }
}

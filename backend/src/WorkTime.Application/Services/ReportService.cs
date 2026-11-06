// Service that creates and renders reports.
// Rules covered: BR12 (an employee may edit his report only until his working day
// ends; afterwards only an administrator may edit), BR14 (daily report is produced
// for a finished day and carries start/end times, the break list, and total worked
// time), BR15 (incomplete days - no end time - are never included in the monthly
// report), BR16 (monthly table employee x days + total column), BR26 (absence days
// are not counted into hours; they are listed separately through the absence
// repository), plus BR5 (days shorter than 15 minutes are excluded).
// BR21 is enforced here as well: an employee may read only his own daily report, so
// GetDailyReportAsync checks the role and the owner of the day and refuses to leak it.
// The monthly table subtracts every counted break of every day (BR4 + BR11), which is
// why it injects IBreakRepository too.
//
// The service asks the repositories for what it needs - a day's breaks by work day
// id, the finished days of a calendar period, the absences of a date range - so no
// method here receives a list of entity ids that only the caller knows.
using WorkTime.Domain;

public class ReportService
{
    private readonly IReportRepository _reports;

    private readonly IWorkDayRepository _workDays;

    // BR8/BR11: the daily report lists the day's breaks and subtracts counted ones.
    private readonly IBreakRepository _breaks;

    // Monthly report needs the calendar month a period closure stands for, and the
    // employee display names (BR16 rows).
    private readonly IPeriodClosureRepository _periodClosures;

    private readonly IUserRepository _users;

    // BR26: absence days are excluded from hours and shown as a separate column.
    private readonly IAbsenceRepository _absences;

    public ReportService(
        IReportRepository reports,
        IWorkDayRepository workDays,
        IBreakRepository breaks,
        IPeriodClosureRepository periodClosures,
        IUserRepository users,
        IAbsenceRepository absences)
    {
        _reports = reports;
        _workDays = workDays;
        _breaks = breaks;
        _periodClosures = periodClosures;
        _users = users;
        _absences = absences;
    }

    // BR12: creates a report for a finished day, or updates the text of an existing
    // one. The report of the day is looked up through IReportRepository.GetForWorkDayAsync,
    // so the caller does not have to know its id. employeeId is kept for the BR21/BR22
    // role gate: an employee touches only his own report.
    public async Task<Report> CreateOrUpdateReportAsync(
        Guid editorId,
        Guid workDayId,
        string text,
        CancellationToken cancellationToken)
    {
        var workDay = await _workDays.GetByIdAsync(workDayId, cancellationToken);
        if (workDay == null)
        {
            throw new EntityNotFoundException(workDayId, "WorkDay");
        }

        var editor = await _users.GetByIdAsync(editorId, cancellationToken);
        if (editor == null)
        {
            throw new EntityNotFoundException(editorId, "User");
        }

        // BR21/BR22: an employee touches only his own report.
        if (workDay.UserId != editorId && editor.Role != UserRole.Admin && editor.Role != UserRole.Manager)
        {
            throw new BusinessRuleException("BR21", "[RU] You may edit only your own report.");
        }

        var existing = await _reports.GetForWorkDayAsync(workDayId, cancellationToken);

        if (existing == null)
        {
            // BR14: the daily report exists only once the working day has finished.
            if (workDay.EndTime == null || workDay.Status == WorkDayStatus.Active)
            {
                throw new BusinessRuleException(
                    "BR14",
                    "[RU] The daily report is created only after the working day is finished.");
            }

            var created = new Report
            {
                Id = Guid.NewGuid(),
                WorkDayId = workDayId,
                Text = text,
                CreatedAt = DateTime.Now,
                UpdatedAt = null,
            };

            await _reports.SaveAsync(created, cancellationToken);
            return created;
        }

        var dayIsClosed = workDay.EndTime != null && workDay.Status != WorkDayStatus.Active;
        if (dayIsClosed && editor.Role != UserRole.Admin)
        {
            // BR12: after the end of the working day only the administrator may edit.
            throw new BusinessRuleException(
                "BR12",
                "[RU] Report editing is closed after the working day ended; only an administrator may change it.");
        }

        existing.Text = text;
        existing.UpdatedAt = DateTime.Now;

        await _reports.SaveAsync(existing, cancellationToken);
        return existing;
    }

    // BR14 + BR21: the daily summary of one finished working day, requested by a user.
    // The role of currentUserId is resolved through IUserRepository, so an Employee may
    // read only his own day (BR21), while a Manager and an Administrator see any day
    // (BR19/BR22). The break list itself comes from IBreakRepository.GetByWorkDayIdAsync.
    public async Task<DailyReport?> GetDailyReportAsync(
        Guid currentUserId,
        Guid workDayId,
        CancellationToken cancellationToken)
    {
        var workDay = await _workDays.GetByIdAsync(workDayId, cancellationToken);
        if (workDay == null)
        {
            throw new EntityNotFoundException(workDayId, "WorkDay");
        }

        var currentUser = await _users.GetByIdAsync(currentUserId, cancellationToken);
        if (currentUser == null)
        {
            throw new EntityNotFoundException(currentUserId, "User");
        }

        // BR21: an employee sees only his own reports. Reading another employee's day is
        // blocked instead of silently returning the data.
        if (currentUser.Role == UserRole.Employee && workDay.UserId != currentUserId)
        {
            throw new BusinessRuleException(
                "BR21",
                "[RU] You may see only your own report.");
        }

        // BR15: an incomplete day (no end time) produces no report.
        if (workDay.EndTime == null || workDay.Status == WorkDayStatus.Active)
        {
            return null;
        }

        var daily = new DailyReport
        {
            StartTime = workDay.StartTime,
            EndTime = workDay.EndTime,
            Breaks = new List<ReportBreak>(),
            TotalHours = TimeSpan.Zero,
        };

        var worked = (workDay.EndTime ?? TimeSpan.Zero) - workDay.StartTime;

        var dayBreaks = await _breaks.GetByWorkDayIdAsync(workDayId, cancellationToken);

        foreach (var workBreak in dayBreaks)
        {
            var reportBreak = new ReportBreak
            {
                StartTime = workBreak.StartTime,
                EndTime = workBreak.EndTime,
            };

            daily.Breaks.Add(reportBreak);

            // BR8/BR11: only counted breaks are subtracted from worked time.
            if (workBreak.CountedInReports && workBreak.EndTime != null)
            {
                var duration = workBreak.Duration ?? (workBreak.EndTime.Value - workBreak.StartTime);
                worked -= duration;
            }
        }

        daily.TotalHours = worked;
        return daily;
    }

    // BR16 + BR15 + BR26 + BR5 + BR4/BR11: the monthly table over the calendar month of
    // a period closure. Rows are chosen by the repository, never assembled by the caller:
    // pass departmentId to build that department's active users, Guid.Empty for every
    // active user of the system (the manager/administrator view). The window comes from
    // periodClosureId, or - when it is Guid.Empty - from the active closure of departmentId.
    // Days are read back from IWorkDayRepository.GetFinishedDaysForPeriodAsync and their
    // breaks from IBreakRepository.GetByWorkDayIdAsync, never supplied by the caller.
    public async Task<List<MonthlyReport>> GetMonthlyReportAsync(
        Guid periodClosureId,
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var closure = await TryGetClosureForAsync(periodClosureId, departmentId, cancellationToken);
        if (closure == null)
        {
            return new List<MonthlyReport>();
        }

        var periodStart = FirstCalendarDayOfMonth(closure.Year, closure.Month);
        var periodEnd = LastCalendarDayOfMonth(closure.Year, closure.Month);

        // BR15: the query returns finished days only, so incomplete days never enter.
        var daysOfPeriod = await _workDays.GetFinishedDaysForPeriodAsync(periodStart, periodEnd, cancellationToken);

        // BR26: the absences of the report window come from the repository as a date range.
        var absencesOfPeriod = await _absences.GetByDateRangeAsync(periodStart, periodEnd, cancellationToken);

        // BR16 rows: active users resolved by the repository, not handed in by the caller.
        var employees = await _users.GetActiveEmployeesInDepartmentAsync(departmentId, cancellationToken);

        var result = new List<MonthlyReport>();

        foreach (var employee in employees)
        {
            var monthly = new MonthlyReport
            {
                EmployeeName = employee.FullName,
                HoursByDay = new Dictionary<DateTime, TimeSpan>(),
                TotalHours = TimeSpan.Zero,
            };

            // BR26: days that carry an "Absence" status are listed separately so the
            // frontend can render its own column; distinct calendar dates only.
            foreach (var absence in absencesOfPeriod)
            {
                if (absence.UserId != employee.Id)
                {
                    continue;
                }

                var absenceDayKey = absence.Date.Date;
                if (!monthly.AbsenceDays.Contains(absenceDayKey))
                {
                    monthly.AbsenceDays.Add(absenceDayKey);
                }
            }

            foreach (var workDay in daysOfPeriod)
            {
                if (workDay.UserId != employee.Id)
                {
                    continue;
                }

                // BR5: days shorter than 15 minutes are not counted.
                if (!workDay.CountedInReports)
                {
                    continue;
                }

                // BR26: an absence day is shown in its own column, never as hours.
                if (TryHasAbsenceOn(absencesOfPeriod, employee.Id, workDay.Date))
                {
                    continue;
                }

                // BR4 + BR11: worked time is the day span minus its counted breaks - the
                // same formula as the daily report, with the breaks read from the repository.
                var span = (workDay.EndTime ?? TimeSpan.Zero) - workDay.StartTime;
                var breakTime = await TrySumCountedBreakTimeAsync(workDay.Id, cancellationToken);
                var worked = span - breakTime;

                var dayKey = workDay.Date.Date;

                if (monthly.HoursByDay.ContainsKey(dayKey))
                {
                    monthly.HoursByDay[dayKey] = monthly.HoursByDay[dayKey] + worked;
                }
                else
                {
                    monthly.HoursByDay[dayKey] = worked;
                }

                monthly.TotalHours = monthly.TotalHours + worked;
            }

            result.Add(monthly);
        }

        return result;
    }

    // Resolves the calendar-month window: an explicit closure, otherwise the active
    // closure of the requested department. No closure and no way to resolve one -> null.
    private async Task<PeriodClosure?> TryGetClosureForAsync(
        Guid periodClosureId,
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        if (periodClosureId != Guid.Empty)
        {
            return await _periodClosures.GetByIdAsync(periodClosureId, cancellationToken);
        }

        if (departmentId != Guid.Empty)
        {
            return await _periodClosures.GetActiveForDepartmentAsync(departmentId, cancellationToken);
        }

        return null;
    }

    // BR4 + BR11 helper: total counted break time of one working day. The breaks come
    // from IBreakRepository.GetByWorkDayIdAsync, so no caller supplies their ids.
    private async Task<TimeSpan> TrySumCountedBreakTimeAsync(
        Guid workDayId,
        CancellationToken cancellationToken)
    {
        var dayBreaks = await _breaks.GetByWorkDayIdAsync(workDayId, cancellationToken);

        var total = TimeSpan.Zero;
        foreach (var workBreak in dayBreaks)
        {
            if (!workBreak.CountedInReports || workBreak.EndTime == null)
            {
                continue;
            }

            total += workBreak.Duration ?? (workBreak.EndTime.Value - workBreak.StartTime);
        }

        return total;
    }

    // First day of the closure month, used as the start of the period window.
    private static DateTime FirstCalendarDayOfMonth(int year, int month)
    {
        return new DateTime(year, month, 1);
    }

    // Last day of the closure month: one day before the first day of the next month.
    private static DateTime LastCalendarDayOfMonth(int year, int month)
    {
        var firstOfNextMonth = month == 12 ? new DateTime(year + 1, 1, 1) : new DateTime(year, month + 1, 1);
        return firstOfNextMonth.AddDays(-1);
    }

    // BR26 helper: does this employee have an absence dated on this calendar day.
    private static bool TryHasAbsenceOn(IEnumerable<Absence> absences, Guid employeeId, DateTime date)
    {
        var needle = date.Date;

        foreach (var absence in absences)
        {
            var absenceDay = absence.Date.Date;
            if (absence.UserId == employeeId && absenceDay == needle)
            {
                return true;
            }
        }

        return false;
    }
}

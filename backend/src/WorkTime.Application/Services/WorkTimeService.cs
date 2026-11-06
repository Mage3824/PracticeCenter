// Service that manages the working day lifecycle: start / finish / auto-finish.
// Rules covered: BR1 (one open day per employee), BR2 (finish only after start), BR3
// (auto-finish at 23:59:59 with an "Auto-finish" event-log entry), BR4 + BR11
// (worked time = (end - start) - sum of counted breaks), BR5 (a day shorter than
// 15 minutes is not counted in reports), BR7 (open break closed before the day),
// BR27 (absence status blocks "Start work").
//
// The service resolves the breaks of a day itself through
// IBreakRepository.GetByWorkDayIdAsync and the absence of a date through
// IAbsenceRepository.GetForEmployeeOnDateAsync, so no method here receives a list
// of break ids from its caller.
using WorkTime.Domain;

public class WorkTimeService
{
    private readonly IWorkDayRepository _workDays;

    private readonly IBreakRepository _breaks;

    private readonly IAbsenceRepository _absences;

    // BR3: the "Auto-finish" entry written into the event log.
    private readonly IAuditLogRepository _auditLogs;

    public WorkTimeService(
        IWorkDayRepository workDays,
        IBreakRepository breaks,
        IAbsenceRepository absences,
        IAuditLogRepository auditLogs)
    {
        _workDays = workDays;
        _breaks = breaks;
        _absences = absences;
        _auditLogs = auditLogs;
    }

    // BR3: the moment of the calendar day the system auto-finishes an open shift at.
    private static readonly TimeSpan EndOfCalendarDay = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);

    public async Task<WorkDay> StartWorkDayAsync(
        Guid employeeId,
        DateTime date,
        TimeSpan startTime,
        CancellationToken cancellationToken)
    {
        // BR27: an absence set for this calendar date blocks "Start work". The lookup
        // is a plain date/owner query now, so it no longer depends on a wired period
        // closure. Absence.CreatedByUserId tells the tooltip whom to name (BR27).
        var blockingAbsences = await _absences.GetForEmployeeOnDateAsync(employeeId, date, cancellationToken);
        foreach (var absence in blockingAbsences)
        {
            throw new BusinessRuleException(
                "BR27",
                "[RU] Absence status [" + AbsenceReasonText(absence.Reason)
                + "] is set for this date - contact manager to remove it. Localize BR27 tooltip.");
        }

        // BR1: an employee can start the working day only once per calendar date, so a
        // second "Start work" while a shift is open on that date is blocked. The question
        // is answered straight by the work-day repository: no period closure has to exist
        // for this guard to hold (BR3 also allows a new calendar day after midnight).
        var openDays = await _workDays.GetActiveForEmployeeOnDateAsync(employeeId, date, cancellationToken);
        if (openDays.Count > 0)
        {
            throw new BusinessRuleException(
                "BR1",
                "[RU] Work day already open for this employee - finish it before starting a new one (BR1).");
        }

        var workDay = new WorkDay
        {
            Id = Guid.NewGuid(),
            UserId = employeeId,
            Date = date.Date,
            StartTime = startTime,
            EndTime = null,
            Status = WorkDayStatus.Active,
            CountedInReports = true,
            IsBreakLimitExceeded = false,
        };

        await _workDays.SaveAsync(workDay, cancellationToken);
        return workDay;
    }

    public async Task<WorkDay> FinishWorkDayAsync(
        Guid employeeId,
        Guid workDayId,
        TimeSpan endTime,
        CancellationToken cancellationToken)
    {
        var workDay = await _workDays.GetByIdAsync(workDayId, cancellationToken);
        if (workDay == null)
        {
            throw new EntityNotFoundException(workDayId, "WorkDay");
        }

        if (workDay.UserId != employeeId)
        {
            throw new BusinessRuleException("BR21", "This working day belongs to another employee.");
        }

        // BR2: "Finish work" is unavailable until the day has started and is still open.
        if (workDay.Status != WorkDayStatus.Active || workDay.EndTime != null)
        {
            throw new BusinessRuleException(
                "BR2",
                "The working day has not started, or it is already finished.");
        }

        // BR7: a still-open break is finished before the day is closed.
        await FinishOpenBreaksAsync(workDayId, endTime, cancellationToken);

        // Правка аудита 3.2/3.3: мьютекс убран, правка дня идёт через await без блокирующих вызовов.
        workDay.EndTime = endTime;
        workDay.Status = WorkDayStatus.Finished;

        var workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);

        // BR5: less than 15 minutes worked means the day is not counted in reports.
        workDay.CountedInReports = workedTime >= WorkTimeRules.MinDayDuration;

        await _workDays.SaveAsync(workDay, cancellationToken);
        return workDay;
    }

    // BR3: automatic closing of an open shift at 23:59:59 plus the event-log entry.
    public async Task<WorkDay> AutoFinishWorkDayAsync(
        Guid employeeId,
        Guid workDayId,
        CancellationToken cancellationToken)
    {
        var workDay = await _workDays.GetByIdAsync(workDayId, cancellationToken);
        if (workDay == null)
        {
            throw new EntityNotFoundException(workDayId, "WorkDay");
        }

        if (workDay.Status != WorkDayStatus.Active || workDay.EndTime != null)
        {
            throw new BusinessRuleException(
                "BR2",
                "The working day is not open, so there is nothing to auto-finish.");
        }

        await FinishOpenBreaksAsync(workDayId, EndOfCalendarDay, cancellationToken);

        // Правка аудита 3.2/3.3: те же общие объекты, что и в FinishWorkDayAsync; мьютекс убран.
        string previousEndTime;
        TimeSpan workedTime;

        previousEndTime = workDay.EndTime == null ? string.Empty : workDay.EndTime.Value.ToString();
        workDay.EndTime = EndOfCalendarDay;
        workDay.Status = WorkDayStatus.AutoFinished;

        workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);
        workDay.CountedInReports = workedTime >= WorkTimeRules.MinDayDuration;

        await _workDays.SaveAsync(workDay, cancellationToken);

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = employeeId,
            Action = AuditAction.Update,
            EntityType = "WorkDay",
            EntityId = workDay.Id,
            OldValue = previousEndTime,
            NewValue = "Auto-finish: EndTime = " + EndOfCalendarDay.ToString(),
            Timestamp = DateTime.Now,
        };

        await _auditLogs.SaveAsync(auditLog, cancellationToken);
        return workDay;
    }

    // BR7 helper: closes every still-open break of this day at the given moment. The
    // day's breaks are read back from the repository, so no ids are supplied by the
    // caller and the employee is not needed to find them.
    private async Task FinishOpenBreaksAsync(
        Guid workDayId,
        TimeSpan endTime,
        CancellationToken cancellationToken)
    {
        var dayBreaks = await _breaks.GetByWorkDayIdAsync(workDayId, cancellationToken);

        // Правка аудита 3.2/3.3: мьютекс убран, сохранение перерывов дня идёт через await.
        foreach (var workBreak in dayBreaks)
        {
            if (workBreak.EndTime != null)
            {
                continue;
            }

            var duration = endTime - workBreak.StartTime;
            workBreak.EndTime = endTime;
            workBreak.Duration = duration;

            // BR8: a break shorter than 5 minutes is not counted anywhere.
            workBreak.CountedInReports = duration >= WorkTimeRules.MinBreakDuration;

            await _breaks.SaveAsync(workBreak, cancellationToken);
        }
    }

    // BR4 + BR11: worked time = (end - start) - sum of counted break durations. The
    // breaks come from IBreakRepository.GetByWorkDayIdAsync; a break that was still
    // open when the day closed has already been saved as finished by FinishOpenBreaksAsync.
    private async Task<TimeSpan> CalculateWorkedTimeAsync(
        WorkDay workDay,
        CancellationToken cancellationToken)
    {
        var elapsed = (workDay.EndTime ?? TimeSpan.Zero) - workDay.StartTime;

        var dayBreaks = await _breaks.GetByWorkDayIdAsync(workDay.Id, cancellationToken);

        var breaksTotal = TimeSpan.Zero;
        foreach (var workBreak in dayBreaks)
        {
            if (!workBreak.CountedInReports || workBreak.EndTime == null)
            {
                continue;
            }

            var duration = workBreak.Duration ?? (workBreak.EndTime.Value - workBreak.StartTime);
            breaksTotal += duration;
        }

        return elapsed - breaksTotal;
    }

    // Russian label of the absence reason for the BR27 tooltip message.
    private static string AbsenceReasonText(AbsenceReason reason)
    {
        switch (reason)
        {
            case AbsenceReason.Sick:
                return "Sickness";
            case AbsenceReason.Leave:
                return "Vacation";
            case AbsenceReason.DayOff:
                return "Day off";
            default:
                return "Absence";
        }
    }
}

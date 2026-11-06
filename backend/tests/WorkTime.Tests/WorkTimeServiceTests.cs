// xUnit tests for WorkTimeService (src/WorkTime.Application/Services/WorkTimeService.cs).
// Covered rules: BR1 (a second open work day is blocked), BR2 ("Finish work" only on an
// open day - two scenarios so both guards of the check are exercised) plus the adjacent
// case where no WorkDay entity exists at all, which raises EntityNotFoundException rather
// than BusinessRuleException, BR3 (an open shift is auto-finished at 23:59:59 and the act
// is written into the event log), BR4 + BR11 (worked time = day span minus the counted breaks
// of the day), BR5 (a day shorter than 15 minutes is simply not counted, it does not throw),
// BR7 (a break that is still open gets closed before the day is finished) and BR27 (an
// absence for the date blocks "Start work").
//
// Every [Fact] builds its own world: the InMemory* repositories are instantiated in the
// Arrange phase and injected into the service constructor. Nothing is mocked by hand.
// Wiring that the repositories need for a rule to be reachable at all: none anymore -
// BR1 is answered by GetActiveForEmployeeOnDateAsync, a plain employee + calendar-date
// query, so WorkTimeService no longer injects users or period closures and a test does
// not have to plant a PeriodClosure for the guard to hold. InMemoryWorkDayRepository
// still takes the closure repository in its constructor because Stage 2 queries (the
// month-scoped GetActiveForEmployeeAsync) are built on it.

public class WorkTimeServiceTests
{
    [Fact]
    public async Task StartWorkDay_SecondOpenDayThrowsBusinessRuleException()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var departmentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var closureId = Guid.NewGuid();
        await periodClosures.SaveAsync(
            new PeriodClosure { Id = closureId, DepartmentId = departmentId, Year = 2024, Month = 3, IsClosed = false },
            ct);

        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = departmentId,
                IsActive = true,
            },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        // The employee already has one open day in the active period (2024-03).
        await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);

        // Act & Assert: BR1 - a second "Start work" while one day is open.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(10), ct));

        Assert.Equal("BR1", ex.Code);
    }

    [Fact]
    public async Task FinishWorkDay_NeverStartedDayThrowsBusinessRuleException()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        // A day record that StartWorkDayAsync never opened: its status is not Active,
        // so the BR2 guard (Status != Active) rejects "Finish work".
        var notStartedDayId = Guid.NewGuid();
        await workDays.SaveAsync(
            new WorkDay
            {
                Id = notStartedDayId,
                UserId = employeeId,
                Date = new DateTime(2024, 3, 10),
                StartTime = TimeSpan.Zero,
                EndTime = null,
                Status = WorkDayStatus.Finished,
                CountedInReports = true,
                IsBreakLimitExceeded = false,
            },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.FinishWorkDayAsync(employeeId, notStartedDayId, TimeSpan.FromHours(17), ct));

        Assert.Equal("BR2", ex.Code);
    }

    [Fact]
    public async Task FinishWorkDay_DayAlreadyFinishedThrowsBusinessRuleException()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        var workDay = await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        await service.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(17), ct);

        // Act & Assert: BR2 - the day is closed (EndTime set, Status Finished), so there is
        // nothing left to finish.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(18), ct));

        Assert.Equal("BR2", ex.Code);
    }

    [Fact]
    public async Task FinishWorkDay_WorkDayEntityDoesNotExistThrowsEntityNotFoundException()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        // The employee never started a day, so the repository holds no WorkDay entity at
        // all: "Finish work" has nothing to read back and reports a missing entity rather
        // than a business-rule violation.
        var unknownWorkDayId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(async () =>
            await service.FinishWorkDayAsync(employeeId, unknownWorkDayId, TimeSpan.FromHours(17), ct));

        Assert.Equal(unknownWorkDayId, ex.EntityId);
        Assert.Equal("WorkDay", ex.EntityType);
    }

    [Fact]
    public async Task FinishWorkDay_CountedBreakIsSubtractedFromWorkedTime()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();
        var reports = new InMemoryReportRepository();

        var departmentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = departmentId,
                IsActive = true,
            },
            ct);

        var closureId = Guid.NewGuid();
        await periodClosures.SaveAsync(
            new PeriodClosure { Id = closureId, DepartmentId = departmentId, Year = 2024, Month = 3, IsClosed = false },
            ct);

        var workTimeService = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var breakService = new BreakService(breaks, workDays);
        var reportService = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        // Work day 09:00 - 17:00 with one counted break 12:00 - 13:00.
        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        var workBreak = await breakService.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(12), ct);
        await breakService.FinishBreakAsync(workBreak.Id, TimeSpan.FromHours(13), employeeId, ct);

        // Act
        var finished = await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(17), ct);
        var daily = await reportService.GetDailyReportAsync(employeeId, workDay.Id, ct);

        // Assert: BR4 + BR11 - (17:00 - 09:00) span minus the one counted break.
        Assert.Equal(TimeSpan.FromHours(17), finished.EndTime);
        Assert.True(finished.CountedInReports);
        Assert.NotNull(daily);
        Assert.Single(daily.Breaks);
        Assert.Equal(TimeSpan.FromHours(7), daily.TotalHours);
    }

    [Fact]
    public async Task FinishWorkDay_LessThanFifteenMinutesSetsCountedInReportsToFalse()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        var workDay = await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);

        // Act: BR5 - a ten minute day must not throw, it is only excluded from reports.
        var result = await service.FinishWorkDayAsync(
            employeeId,
            workDay.Id,
            TimeSpan.FromHours(9) + TimeSpan.FromMinutes(10),
            ct);

        // Assert
        Assert.False(result.CountedInReports);
        Assert.Equal(TimeSpan.FromHours(9) + TimeSpan.FromMinutes(10), result.EndTime);
        Assert.True(result.Status == WorkDayStatus.Finished);
    }

    [Fact]
    public async Task StartWorkDay_AbsenceForThisDateThrowsBusinessRuleException()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var departmentId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = departmentId,
                IsActive = true,
            },
            ct);

        var managerId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = managerId,
                Login = "manager",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Maria Manager",
                Role = UserRole.Manager,
                DepartmentId = departmentId,
                IsActive = true,
            },
            ct);

        var closureId = Guid.NewGuid();
        await periodClosures.SaveAsync(
            new PeriodClosure { Id = closureId, DepartmentId = departmentId, Year = 2024, Month = 3, IsClosed = false },
            ct);

        // The manager sets an absence status for this employee on 2024-03-10 (BR25).
        var absenceService = new AbsenceService(users, absences, periodClosures);
        await absenceService.SetAbsenceAsync(managerId, employeeId, new DateTime(2024, 3, 10), AbsenceReason.Sick, ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        // Act & Assert: BR27 - "Start work" is blocked while an absence stands for the date.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct));

        Assert.Equal("BR27", ex.Code);
    }

    [Fact]
    public async Task AutoFinishWorkDay_ClosesOpenShiftAndWritesAutoFinishLog()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);

        // An open shift that nobody closed manually. No period closure is wired here on
        // purpose: BR1/BR3 must hold without one (this also proves the old leak).
        var workDay = await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);

        // Act: BR3 - the system closes the shift at 23:59:59 and logs that it did so.
        var finished = await service.AutoFinishWorkDayAsync(employeeId, workDay.Id, ct);

        // Assert: the day is closed by the system, not by the employee.
        Assert.True(finished.Status == WorkDayStatus.AutoFinished);
        Assert.Equal(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59), finished.EndTime);
        Assert.True(finished.CountedInReports);

        // BR3: the event log must hold an "Auto-finish" entry about this very day.
        var history = await auditLogs.GetHistoryForEntityAsync(workDay.Id, ct);
        var entries = new List<AuditLog>(history);
        Assert.Equal(1, entries.Count);

        var entry = entries[0];
        Assert.Equal(workDay.Id, entry.EntityId);
        Assert.Equal("WorkDay", entry.EntityType);
        Assert.True(entry.Action == AuditAction.Update);
        Assert.NotNull(entry.NewValue);
        Assert.True((entry.NewValue ?? string.Empty).Contains("Auto-finish"));

        // BR29: the log names who caused the auto-finish.
        Assert.Equal(employeeId, entry.UserId);
    }

    [Fact]
    public async Task FinishWorkDay_AutoClosesStillOpenBreak()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var workDays = new InMemoryWorkDayRepository(periodClosures);
        var breaks = new InMemoryBreakRepository(workDays);
        var absences = new InMemoryAbsenceRepository(periodClosures);
        var auditLogs = new InMemoryAuditLogRepository();

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var workTimeService = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var breakService = new BreakService(breaks, workDays);

        // Work day 09:00 - ... with a break that was started at 12:00 and never finished.
        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        var openBreak = await breakService.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(12), ct);

        // Act: BR7 - the day is closed while the break is still open.
        var finished = await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(16), ct);

        // Assert: the break was closed at the moment of the day, and it is read back in
        // that state from the repository (the employee never called "Finish break").
        var storedBreak = await breaks.GetByIdAsync(openBreak.Id, ct);
        Assert.NotNull(storedBreak);
        Assert.Equal(TimeSpan.FromHours(16), storedBreak?.EndTime);
        Assert.Equal(TimeSpan.FromHours(4), storedBreak?.Duration);
        Assert.True(storedBreak?.CountedInReports == true);

        // Seven worked hours minus the four auto-closed break hours is still counted.
        Assert.True(finished.CountedInReports);
        Assert.Equal(TimeSpan.FromHours(16), finished.EndTime);
    }
}

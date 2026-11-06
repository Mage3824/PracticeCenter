// xUnit tests for ReportService (src/WorkTime.Application/Services/ReportService.cs).
// Covered rules: BR12 (an employee may edit his report only until the working day ends;
// afterwards only an administrator may change it - a manager is rejected as well), BR14
// (the daily report of a finished day lists the day's breaks and carries the total worked
// time with counted breaks subtracted), BR15 (days without an end time never enter the
// monthly table) and BR16 + BR5 (the monthly row totals only days of at least 15 minutes).
// The three rules that were silently broken before the fix round are covered too: BR21 (an
// employee may read only his own daily report - a manager/administrator sees any day), BR4 +
// BR11 in the monthly table (every counted break of every day is subtracted, so the total is
// worked time and not raw span) and BR26 (a day with an absence status contributes nothing to
// the totals and its date appears in MonthlyReport.AbsenceDays instead).

public class ReportServiceTests
{
    [Fact]
    public async Task CreateOrUpdateReport_EmployeeAfterDayFinishedThrowsBusinessRuleException()
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
        var service = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        // The employee works a day and closes it, then writes his report while it is his
        // to edit.
        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(17), ct);
        await service.CreateOrUpdateReportAsync(employeeId, workDay.Id, "My own report", ct);

        // Act & Assert: BR12 - the day is over, so the employee may no longer edit.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.CreateOrUpdateReportAsync(employeeId, workDay.Id, "Trying to change it", ct));

        Assert.Equal("BR12", ex.Code);
    }

    [Fact]
    public async Task CreateOrUpdateReport_AdministratorStillEditsClosedDayReport()
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

        var adminId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = adminId,
                Login = "admin",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ada Administrator",
                Role = UserRole.Admin,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var workTimeService = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var service = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(17), ct);
        await service.CreateOrUpdateReportAsync(employeeId, workDay.Id, "Employee text", ct);

        // Act: the administrator changes the report of the closed day.
        var updated = await service.CreateOrUpdateReportAsync(adminId, workDay.Id, "Corrected by administrator", ct);

        // Assert: BR12 - only the administrator may edit after the day ended.
        Assert.Equal("Corrected by administrator", updated.Text);
        Assert.True(updated.UpdatedAt != null);
        Assert.Equal(workDay.Id, updated.WorkDayId);
    }

    [Fact]
    public async Task CreateOrUpdateReport_ManagerAfterDayFinishedThrowsBusinessRuleException()
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
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var workTimeService = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var service = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(17), ct);
        await service.CreateOrUpdateReportAsync(employeeId, workDay.Id, "Employee text", ct);

        // Act & Assert: BR12 - the manager may comment but not edit a closed day's report.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.CreateOrUpdateReportAsync(managerId, workDay.Id, "Manager tries to edit", ct));

        Assert.Equal("BR12", ex.Code);
    }

    [Fact]
    public async Task GetDailyReport_TotalsSubtractCountedBreaksOnly()
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
        var service = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        // A day 08:00 - 16:00 (8 hours) with one counted break (12:00 - 13:00) and one
        // break that BR8 excludes from the totals (14:00 - 14:02).
        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(8), ct);

        var countedBreak = await breakService.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(12), ct);
        await breakService.FinishBreakAsync(countedBreak.Id, TimeSpan.FromHours(13), employeeId, ct);

        var shortBreak = await breakService.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(14), ct);
        await breakService.FinishBreakAsync(shortBreak.Id, TimeSpan.FromHours(14) + TimeSpan.FromMinutes(2), employeeId, ct);

        await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(16), ct);

        // Act
        var daily = await service.GetDailyReportAsync(employeeId, workDay.Id, ct);

        // Assert: BR14 - the report is produced for a finished day and both breaks are
        // listed, but only the counted one is subtracted (8h - 1h).
        Assert.NotNull(daily);
        Assert.Equal(TimeSpan.FromHours(8), daily.StartTime);
        Assert.Equal(TimeSpan.FromHours(16), daily.EndTime);
        Assert.Equal(2, daily.Breaks.Count);
        Assert.Equal(TimeSpan.FromHours(7), daily.TotalHours);
    }

    [Fact]
    public async Task GetMonthlyReport_SkipsIncompleteDaysAndDaysUnderFifteenMinutes()
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

        // The active period closure stands for the calendar month 2024-03.
        var closureId = Guid.NewGuid();
        await periodClosures.SaveAsync(
            new PeriodClosure { Id = closureId, DepartmentId = departmentId, Year = 2024, Month = 3, IsClosed = false },
            ct);

        var service = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var reportService = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        // 2024-03-01: a normal eight hour day - must be counted.
        var fullDay = await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 1), TimeSpan.FromHours(8), ct);
        await service.FinishWorkDayAsync(employeeId, fullDay.Id, TimeSpan.FromHours(16), ct);

        // 2024-03-02: a ten minute day - BR5 excludes it from the totals.
        var tooShortDay = await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 2), TimeSpan.FromHours(9), ct);
        await service.FinishWorkDayAsync(employeeId, tooShortDay.Id, TimeSpan.FromHours(9) + TimeSpan.FromMinutes(10), ct);

        // 2024-03-05: a day that was started but never finished - BR15 excludes it.
        await service.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 5), TimeSpan.FromHours(10), ct);

        // Act
        var monthly = await reportService.GetMonthlyReportAsync(Guid.Empty, departmentId, ct);

        // Assert: BR16 - one row per active employee of the department (resolved by the
        // repository), holding only the counted day.
        Assert.Single(monthly);
        var row = monthly[0];
        Assert.Equal("Ivan Petrov", row.EmployeeName);
        Assert.Equal(TimeSpan.FromHours(8), row.TotalHours);
        Assert.Single(row.HoursByDay);

        Assert.False(row.HoursByDay.ContainsKey(new DateTime(2024, 3, 2)));
        Assert.False(row.HoursByDay.ContainsKey(new DateTime(2024, 3, 5)));
    }

    [Fact]
    public async Task GetDailyReport_EmployeeAsksForAnotherEmployeeReportThrowsBusinessRuleException()
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

        // The colleague owns the working day that is being requested.
        var colleagueId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = colleagueId,
                Login = "kollega",
                // BR34: фикстура хранит хеш PasswordHasher, открытый пароль не записывается.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Kollega Ivanov",
                Role = UserRole.Employee,
                DepartmentId = departmentId,
                IsActive = true,
            },
            ct);

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

        var workTimeService = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var reportService = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        // The colleague works and closes his own day.
        var colleagueDay = await workTimeService.StartWorkDayAsync(colleagueId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        await workTimeService.FinishWorkDayAsync(colleagueId, colleagueDay.Id, TimeSpan.FromHours(17), ct);

        // Act & Assert: BR21 - an employee may see only his own reports, so reading the
        // colleague's daily report is blocked instead of leaking the data.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await reportService.GetDailyReportAsync(employeeId, colleagueDay.Id, ct));

        Assert.Equal("BR21", ex.Code);
    }

    [Fact]
    public async Task GetMonthlyReport_SubtractsCountedBreaksFromEveryDay()
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

        // 2024-03-01: an eight hour day (08:00 - 16:00) with one counted break 12:00 - 13:00.
        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 1), TimeSpan.FromHours(8), ct);
        var workBreak = await breakService.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(12), ct);
        await breakService.FinishBreakAsync(workBreak.Id, TimeSpan.FromHours(13), employeeId, ct);
        await workTimeService.FinishWorkDayAsync(employeeId, workDay.Id, TimeSpan.FromHours(16), ct);

        // Act
        var monthly = await reportService.GetMonthlyReportAsync(Guid.Empty, departmentId, ct);

        // Assert: BR4 + BR11 - the monthly table uses the same formula as the daily report,
        // so the counted break is subtracted (8h span - 1h break).
        Assert.Single(monthly);
        var row = monthly[0];
        Assert.Equal(TimeSpan.FromHours(7), row.TotalHours);
        Assert.Equal(TimeSpan.FromHours(7), row.HoursByDay[new DateTime(2024, 3, 1)]);
    }

    [Fact]
    public async Task GetMonthlyReport_ListsAbsenceDaysAndExcludesTheirHours()
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

        // The manager lives in another department, so he never becomes a row of the BR16 table.
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
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        var closureId = Guid.NewGuid();
        await periodClosures.SaveAsync(
            new PeriodClosure { Id = closureId, DepartmentId = departmentId, Year = 2024, Month = 3, IsClosed = false },
            ct);

        var workTimeService = new WorkTimeService(workDays, breaks, absences, auditLogs);
        var absenceService = new AbsenceService(users, absences, periodClosures);
        var reportService = new ReportService(reports, workDays, breaks, periodClosures, users, absences);

        // Two finished days: 2024-03-01 (8h) and 2024-03-04 (6h).
        var firstDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 1), TimeSpan.FromHours(8), ct);
        await workTimeService.FinishWorkDayAsync(employeeId, firstDay.Id, TimeSpan.FromHours(16), ct);

        var secondDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 4), TimeSpan.FromHours(9), ct);
        await workTimeService.FinishWorkDayAsync(employeeId, secondDay.Id, TimeSpan.FromHours(15), ct);

        // BR28: the manager changes the status of the day of 2024-03-04 to "vacation".
        await absenceService.SetAbsenceAsync(managerId, employeeId, new DateTime(2024, 3, 4), AbsenceReason.Leave, ct);

        // Act
        var monthly = await reportService.GetMonthlyReportAsync(Guid.Empty, departmentId, ct);

        // Assert: BR26 - the absence day is never counted as hours, and it is carried in
        // its own list for the separate column of the table.
        Assert.Single(monthly);
        var row = monthly[0];
        Assert.Equal(TimeSpan.FromHours(8), row.TotalHours);
        Assert.Equal(1, row.HoursByDay.Count);
        Assert.True(row.HoursByDay.ContainsKey(new DateTime(2024, 3, 1)));
        Assert.False(row.HoursByDay.ContainsKey(new DateTime(2024, 3, 4)));
        Assert.Equal(1, row.AbsenceDays.Count);
        Assert.True(row.AbsenceDays.Contains(new DateTime(2024, 3, 4)));
    }
}

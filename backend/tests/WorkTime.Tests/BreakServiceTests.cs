// xUnit tests for BreakService (src/WorkTime.Application/Services/BreakService.cs).
// Covered rules: BR8 (a break shorter than 5 minutes is not counted and not saved in
// reports, and it is NOT an error), BR9 (total break time per day passed the 4 hour cap
// marks WorkDay.IsBreakLimitExceeded - never a rejection), BR10 (only one open break at
// a time) and BR6 (a break only exists inside an open working day).
//
// The repositories are instantiated in the Arrange phase and injected into the service.
// Wiring that matters here: InMemoryBreakRepository(workDays) - GetUnfinishedForEmployeeAsync
// resolves Break.WorkDayId to the owning employee, which is exactly what makes BR10 reachable.

public class BreakServiceTests
{
    [Fact]
    public async Task FinishBreak_ShorterThanFiveMinutesSetsCountedInReportsToFalse()
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
        var service = new BreakService(breaks, workDays);

        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);
        var workBreak = await service.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(12), ct);

        // Act: BR8 - a three minute break must not throw.
        var result = await service.FinishBreakAsync(workBreak.Id, TimeSpan.FromHours(12) + TimeSpan.FromMinutes(3), employeeId, ct);

        // Assert
        Assert.False(result.CountedInReports);
        Assert.Equal(TimeSpan.FromMinutes(3), result.Duration);

        var storedDay = await workDays.GetByIdAsync(workDay.Id, ct);
        Assert.NotNull(storedDay);
        Assert.False(storedDay.IsBreakLimitExceeded);
    }

    [Fact]
    public async Task FinishBreak_LongerThanFourHoursSetsBreakLimitExceededOnWorkDay()
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
        var service = new BreakService(breaks, workDays);

        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(8), ct);
        var workBreak = await service.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(9), ct);

        // Act: BR9 - a five hour break passes the 4 hour cap. The system does not reject it.
        var result = await service.FinishBreakAsync(workBreak.Id, TimeSpan.FromHours(14), employeeId, ct);

        // Assert
        Assert.Equal(TimeSpan.FromHours(5), result.Duration);
        Assert.True(result.CountedInReports);

        var storedDay = await workDays.GetByIdAsync(workDay.Id, ct);
        Assert.NotNull(storedDay);
        Assert.True(storedDay.IsBreakLimitExceeded);
    }

    [Fact]
    public async Task StartBreak_WhenFourHourCapAlreadyTakenSetsBreakLimitExceeded()
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
        var service = new BreakService(breaks, workDays);

        // The day already carries exactly 4 hours of counted break time (09:00 - 13:00).
        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(8), ct);
        var firstBreak = await service.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(9), ct);
        await service.FinishBreakAsync(firstBreak.Id, TimeSpan.FromHours(13), employeeId, ct);

        // Act: BR9 on the start path - opening one more break over the cap is not an error.
        var secondBreak = await service.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(14), ct);

        // Assert
        var storedDay = await workDays.GetByIdAsync(workDay.Id, ct);
        Assert.NotNull(storedDay);
        Assert.True(storedDay.IsBreakLimitExceeded);
        Assert.True(secondBreak.CountedInReports);
        Assert.Equal(workDay.Id, secondBreak.WorkDayId);
    }

    [Fact]
    public async Task StartBreak_AnotherBreakStillOpenThrowsBusinessRuleException()
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
        var service = new BreakService(breaks, workDays);

        var workDay = await workTimeService.StartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), TimeSpan.FromHours(9), ct);

        // The employee already has one open (unfinished) break.
        await service.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(12), ct);

        // Act & Assert: BR10 - a second "Start break" while the first one is still open.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.StartBreakAsync(employeeId, workDay.Id, TimeSpan.FromHours(14), ct));

        Assert.Equal("BR10", ex.Code);
    }
}

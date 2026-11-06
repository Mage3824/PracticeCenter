// xUnit tests for AbsenceService (src/WorkTime.Application/Services/AbsenceService.cs).
// Covered rules: BR25 / BR28 (only an administrator or a manager may set or change the
// absence status of an employee - an employee is rejected with BusinessRuleException) and
// the BR27 guard CanStartWorkDayAsync, which reports the same fact as a plain boolean and
// never throws.

public class AbsenceServiceTests
{
    [Fact]
    public async Task SetAbsence_EmployeeAsActorThrowsBusinessRuleException()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var absences = new InMemoryAbsenceRepository(periodClosures);

        var departmentId = Guid.NewGuid();

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

        var service = new AbsenceService(users, absences, periodClosures);

        // Act & Assert: BR25/BR28 - the employee tries to set the status of a colleague.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
            await service.SetAbsenceAsync(employeeId, managerId, new DateTime(2024, 3, 10), AbsenceReason.DayOff, ct));

        Assert.Equal("BR25", ex.Code);
    }

    [Fact]
    public async Task SetAbsence_ManagerSetsStatusForEmployee()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var absences = new InMemoryAbsenceRepository(periodClosures);

        var departmentId = Guid.NewGuid();

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

        var service = new AbsenceService(users, absences, periodClosures);

        // Act: BR25 - a manager may set the absence status with a reason.
        var absence = await service.SetAbsenceAsync(managerId, employeeId, new DateTime(2024, 3, 10), AbsenceReason.Sick, ct);

        // Assert: the status belongs to the employee, is dated on the calendar day, and
        // remembers who set it (BR30 author behind the BR27 tooltip).
        Assert.Equal(employeeId, absence.UserId);
        Assert.Equal(new DateTime(2024, 3, 10), absence.Date);
        Assert.Equal(AbsenceReason.Sick, absence.Reason);
        Assert.True(absence.CreatedByUserId == managerId);

        var readBack = await absences.GetByIdAsync(absence.Id, ct);
        Assert.NotNull(readBack);
        Assert.Equal(absence.Id, readBack.Id);
    }

    [Fact]
    public async Task SetAbsence_AdministratorSetsStatusForEmployee()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var absences = new InMemoryAbsenceRepository(periodClosures);

        var departmentId = Guid.NewGuid();

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

        var service = new AbsenceService(users, absences, periodClosures);

        // Act: BR25 - the administrator may set the status as well.
        var absence = await service.SetAbsenceAsync(adminId, employeeId, new DateTime(2024, 3, 11), AbsenceReason.Leave, ct);

        // Assert
        Assert.Equal(employeeId, absence.UserId);
        Assert.Equal(AbsenceReason.Leave, absence.Reason);
        Assert.True(absence.CreatedByUserId == adminId);
    }

    [Fact]
    public async Task CanStartWorkDay_FalseWhenAbsenceIsSetForTheDate()
    {
        // Arrange
        var ct = CancellationToken.None;

        var periodClosures = new InMemoryPeriodClosureRepository();
        var users = new InMemoryUserRepository();
        var absences = new InMemoryAbsenceRepository(periodClosures);

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

        // The guard reads the absence through the active closure of the department, so the
        // calendar month of the period has to be present.
        var closureId = Guid.NewGuid();
        await periodClosures.SaveAsync(
            new PeriodClosure { Id = closureId, DepartmentId = departmentId, Year = 2024, Month = 3, IsClosed = false },
            ct);

        var service = new AbsenceService(users, absences, periodClosures);
        await service.SetAbsenceAsync(managerId, employeeId, new DateTime(2024, 3, 10), AbsenceReason.DayOff, ct);

        // Act & Assert: BR27 guard - blocked on the absence day, allowed on a clean one.
        var blocked = await service.CanStartWorkDayAsync(employeeId, new DateTime(2024, 3, 10), ct);
        var allowed = await service.CanStartWorkDayAsync(employeeId, new DateTime(2024, 3, 15), ct);

        Assert.False(blocked);
        Assert.True(allowed);
    }
}

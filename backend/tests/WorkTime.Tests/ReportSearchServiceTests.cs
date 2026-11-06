
using WorkTime.Domain;

public class ReportSearchServiceTests
{
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryReportRepository _reports = new();
    private readonly InMemoryWorkDayRepository _workDays;

    public ReportSearchServiceTests()
    {
        var periods = new InMemoryPeriodClosureRepository();
        _workDays = new InMemoryWorkDayRepository(periods);
    }

    // Добавление сотрудника для тестов
    private async Task<User> AddUser(
        UserRole role = UserRole.Employee)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Иван Иванов",
            Login = Guid.NewGuid().ToString(),
            DepartmentId = Guid.NewGuid(),
            Role = role,
            IsActive = true
        };

        await _users.SaveAsync(user, CancellationToken.None);
        return user;
    }

    // Добавление отчёта с рабочим днём
    private async Task AddReport(
        User user, string text, DateTime date)
    {
        var day = new WorkDay
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Date = date,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(18),
            Status = WorkDayStatus.Finished
        };

        await _workDays.SaveAsync(day, CancellationToken.None);

        await _reports.SaveAsync(new Report
        {
            Id = Guid.NewGuid(),
            WorkDayId = day.Id,
            Text = text,
            CreatedAt = date
        }, CancellationToken.None);
    }

    private ReportSearchService CreateService()
    {
        return new ReportSearchService(
            _reports, _workDays, _users);
    }

    [Fact]
    public async Task Search_FiltersByText()
    {
        var manager = await AddUser(UserRole.Manager);
        var employee = await AddUser();

        await AddReport(employee, "Тестирование API",
            new DateTime(2026, 11, 1));

        await AddReport(employee, "Разработка интерфейса",
            new DateTime(2026, 11, 2));

        var result = await CreateService().SearchAsync(
            manager.Id, "api", null, null,
            null, null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Тестирование API", result[0].Text);
    }

    [Fact]
    public async Task Search_FiltersByDate()
    {
        var manager = await AddUser(UserRole.Manager);
        var employee = await AddUser();

        await AddReport(employee, "Первый отчёт",
            new DateTime(2026, 10, 20));

        await AddReport(employee, "Второй отчёт",
            new DateTime(2026, 11, 5));

        var result = await CreateService().SearchAsync(
            manager.Id, null, null, null,
            new DateTime(2026, 11, 1),
            new DateTime(2026, 11, 30),
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Второй отчёт", result[0].Text);
    }

    [Fact]
    public async Task Search_EmployeeSeesOnlyOwnReports()
    {
        var employee1 = await AddUser();
        var employee2 = await AddUser();

        await AddReport(employee1, "Мой отчёт",
            new DateTime(2026, 11, 1));

        await AddReport(employee2, "Чужой отчёт",
            new DateTime(2026, 11, 1));

        var result = await CreateService().SearchAsync(
            employee1.Id, null, null, null,
            null, null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Мой отчёт", result[0].Text);
    }

    [Fact]
    public async Task Search_EmployeeCannotRequestAnotherUser()
    {
        var employee1 = await AddUser();
        var employee2 = await AddUser();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateService().SearchAsync(
                employee1.Id, null, employee2.Id, null,
                null, null, CancellationToken.None));

        Assert.Equal("BR21", error.Code);
    }

    [Fact]
    public async Task Search_FiltersByDepartment()
    {
        var manager = await AddUser(UserRole.Manager);
        var employee1 = await AddUser();
        var employee2 = await AddUser();

        await AddReport(employee1, "Отчёт отдела 1",
            new DateTime(2026, 11, 1));

        await AddReport(employee2, "Отчёт отдела 2",
            new DateTime(2026, 11, 1));

        var result = await CreateService().SearchAsync(
            manager.Id, null, null, employee1.DepartmentId,
            null, null, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Отчёт отдела 1", result[0].Text);
    }

    [Fact]
    public async Task Search_NoMatchesReturnsEmptyList()
    {
        var manager = await AddUser(UserRole.Manager);
        var employee = await AddUser();

        await AddReport(employee, "Разработка API",
            new DateTime(2026, 11, 1));

        var result = await CreateService().SearchAsync(
            manager.Id, "несуществующий текст",
            null, null, null, null, CancellationToken.None);

        Assert.Empty(result);
    }
}

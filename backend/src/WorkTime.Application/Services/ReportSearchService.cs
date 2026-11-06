
using WorkTime.Domain;

public class ReportSearchService
{
    private readonly IReportRepository _reports;
    private readonly IWorkDayRepository _workDays;
    private readonly IUserRepository _users;

    public ReportSearchService(
        IReportRepository reports,
        IWorkDayRepository workDays,
        IUserRepository users)
    {
        _reports = reports;
        _workDays = workDays;
        _users = users;
    }

    public async Task<List<ReportSearchItem>> SearchAsync(
        Guid currentUserId,
        string? text,
        Guid? employeeId,
        Guid? departmentId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        // Получаем пользователя, который выполняет поиск
        var currentUser = await _users.GetByIdAsync(
            currentUserId, cancellationToken);

        if (currentUser == null)
        {
            throw new EntityNotFoundException(
                currentUserId, "User");
        }

        // Сотрудник может искать только свои отчёты
        if (currentUser.Role == UserRole.Employee)
        {
            if (employeeId.HasValue &&
                employeeId.Value != currentUserId)
            {
                throw new BusinessRuleException(
                    "BR21",
                    "Нельзя просматривать чужие отчёты.");
            }

            if (departmentId.HasValue &&
                departmentId.Value != currentUser.DepartmentId)
            {
                throw new BusinessRuleException(
                    "BR21",
                    "Нет доступа к отчётам этого отдела.");
            }

            employeeId = currentUserId;
        }

        var result = new List<ReportSearchItem>();

        // Получаем сохранённые отчёты
        var reports = await _reports.GetAllAsync(
            cancellationToken);

        foreach (var report in reports)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Проверяем текст отчёта
            if (!string.IsNullOrWhiteSpace(text) &&
                !report.Text.Contains(
                    text.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Получаем рабочий день отчёта
            var workDay = await _workDays.GetByIdAsync(
                report.WorkDayId, cancellationToken);

            if (workDay == null)
                continue;

            // Проверяем сотрудника
            if (employeeId.HasValue &&
                workDay.UserId != employeeId.Value)
            {
                continue;
            }

            // Проверяем дату начала периода
            if (from.HasValue &&
                workDay.Date.Date < from.Value.Date)
            {
                continue;
            }

            // Проверяем дату конца периода
            if (to.HasValue &&
                workDay.Date.Date > to.Value.Date)
            {
                continue;
            }

            // Получаем данные сотрудника
            var employee = await _users.GetByIdAsync(
                workDay.UserId, cancellationToken);

            if (employee == null)
                continue;

            // Проверяем отдел
            if (departmentId.HasValue &&
                employee.DepartmentId != departmentId.Value)
            {
                continue;
            }

            // Добавляем подходящий отчёт
            result.Add(new ReportSearchItem
            {
                Id = report.Id,
                WorkDayId = report.WorkDayId,
                EmployeeId = employee.Id,
                EmployeeName = employee.FullName,
                DepartmentId = employee.DepartmentId,
                Date = workDay.Date.Date,
                Text = report.Text,
                CreatedAt = report.CreatedAt
            });
        }

        // Сначала показываем новые отчёты
        return result
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.EmployeeName)
            .ToList();
    }
}

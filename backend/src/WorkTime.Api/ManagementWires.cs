// Проводные формы сущностей управления (пользователи, отделы, комментарии, расчётный период),
// конвертеры сущностей домена и вспомогательные средства для контроллеров этапа 2.
// Формы повторяют таблицы полей api-contracts.md (§5); PasswordHash наружу не отдаётся (BR34).
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WorkTime.Domain;

namespace WorkTime.Api;

public class UserWire
{
    public Guid id { get; set; }
    public string login { get; set; } = string.Empty;
    public string fullName { get; set; } = string.Empty;
    public string role { get; set; } = string.Empty;
    public Guid departmentId { get; set; }
    public bool isActive { get; set; }
    public int failedLoginAttempts { get; set; }
    public string? lockedUntil { get; set; }
    public bool locked { get; set; }
}

public class DepartmentWire
{
    public Guid id { get; set; }
    public string name { get; set; } = string.Empty;
    public string? parentId { get; set; }
}

public class CommentWire
{
    public Guid id { get; set; }
    public Guid reportId { get; set; }
    public Guid managerId { get; set; }
    public string text { get; set; } = string.Empty;
    public string createdAt { get; set; } = string.Empty;
}

public class PeriodClosureWire
{
    public Guid id { get; set; }
    public Guid departmentId { get; set; }
    public int year { get; set; }
    public int month { get; set; }
    public bool isClosed { get; set; }
    public string? closedAt { get; set; }
    public string? closedBy { get; set; }
    public string? reopenReason { get; set; }
    public string? reopenedByUserId { get; set; }
}

// Ответ эндпоинта GET /api/work-days/{employeeId}/can-start (BR27 как состояние кнопки).
public class CanStartWorkDayWire
{
    public bool canStart { get; set; }
}

public static class ManagementWireConversion
{
    public static UserWire TryFromUser(User user)
    {
        var wire = new UserWire();
        wire.id = user.Id;
        wire.login = user.Login;
        wire.fullName = user.FullName;
        wire.role = user.Role.ToString();
        wire.departmentId = user.DepartmentId;
        wire.isActive = user.IsActive;
        wire.failedLoginAttempts = user.FailedLoginAttempts;
        wire.lockedUntil = WireFormats.TryTimestampText(user.LockedUntil);
        wire.locked = user.Locked;
        return wire;
    }

    public static UserWire[] TryFromUsers(IEnumerable<User> users)
    {
        var result = new List<UserWire>();
        foreach (var user in users)
        {
            result.Add(TryFromUser(user));
        }

        return result.ToArray();
    }

    public static DepartmentWire TryFromDepartment(Department department)
    {
        var wire = new DepartmentWire();
        wire.id = department.Id;
        wire.name = department.Name;
        wire.parentId = WireFormats.TryGuidText(department.ParentId);
        return wire;
    }

    public static DepartmentWire[] TryFromDepartments(IEnumerable<Department> departments)
    {
        var result = new List<DepartmentWire>();
        foreach (var department in departments)
        {
            result.Add(TryFromDepartment(department));
        }

        return result.ToArray();
    }

    public static CommentWire TryFromComment(Comment comment)
    {
        var wire = new CommentWire();
        wire.id = comment.Id;
        wire.reportId = comment.ReportId;
        wire.managerId = comment.ManagerId;
        wire.text = comment.Text;
        wire.createdAt = WireFormats.TryTimestampText(comment.CreatedAt);
        return wire;
    }

    public static CommentWire[] TryFromComments(IEnumerable<Comment> comments)
    {
        var result = new List<CommentWire>();
        foreach (var comment in comments)
        {
            result.Add(TryFromComment(comment));
        }

        return result.ToArray();
    }

    public static PeriodClosureWire TryFromPeriodClosure(PeriodClosure closure)
    {
        var wire = new PeriodClosureWire();
        wire.id = closure.Id;
        wire.departmentId = closure.DepartmentId;
        wire.year = closure.Year;
        wire.month = closure.Month;
        wire.isClosed = closure.IsClosed;
        wire.closedAt = WireFormats.TryTimestampText(closure.ClosedAt);
        wire.closedBy = WireFormats.TryGuidText(closure.ClosedBy);
        wire.reopenReason = closure.ReopenReason;
        wire.reopenedByUserId = WireFormats.TryGuidText(closure.ReopenedByUserId);
        return wire;
    }
}

// BR17: Excel из этапа 4 ещё не построен, поэтому экспорт отдаётся как CSV (UTF-8 с BOM,
// разделитель ";" — так файл открывается в Excel на русской локали).
public static class MonthlyReportCsv
{
    public static byte[] TryBuild(MonthlyReportWire[] reports)
    {
        var dayKeys = new List<string>();
        foreach (var report in reports)
        {
            foreach (var key in report.hoursByDay.Keys)
            {
                if (!dayKeys.Contains(key))
                {
                    dayKeys.Add(key);
                }
            }
        }

        dayKeys.Sort(StringComparer.Ordinal);

        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.Append("employee;").Append(string.Join(";", dayKeys)).Append(";total_minutes;absences");
        builder.Append("\r\n");

        foreach (var report in reports)
        {
            builder.Append(Escape(report.employeeName));

            foreach (var key in dayKeys)
            {
                var minutes = report.hoursByDay.TryGetValue(key, out var value) ? value : 0L;
                builder.Append(';').Append(minutes.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(';').Append(report.totalMinutes.ToString(CultureInfo.InvariantCulture));
            builder.Append(';').Append(Escape(string.Join("|", report.absenceDays)));
            builder.Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string Escape(string text)
    {
        if (!text.Contains(';') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n'))
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

// BR23/BR31: административные изменения пишутся в журнал событий (кто, что, до/после, время).
public static class AuditTrail
{
    public static async Task TryWriteAsync(
        IAuditLogRepository auditLogs,
        Guid actorId,
        AuditAction action,
        string entityType,
        Guid entityId,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken)
    {
        var entry = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = DateTime.UtcNow,
        };

        await auditLogs.SaveAsync(entry, cancellationToken);
    }
}

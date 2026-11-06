// Domain entities.
// Time-of-day / durations are TimeSpan, dates are DateTime.
// Nullable end times (day not closed yet) are TimeSpan?.

namespace WorkTime.Domain;

public class User
{
    public Guid Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public Guid DepartmentId { get; set; }
    public bool IsActive { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool Locked { get; set; }
}

public class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}

public class WorkDay
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public WorkDayStatus Status { get; set; }
    public bool CountedInReports { get; set; } = true;

    // BR9: the total break time of the day passed the 4 hour cap. The system does
    // not reject such a break; it records the fact so the manager is notified and
    // the administrator can review the day.
    public bool IsBreakLimitExceeded { get; set; }
}

public class Break
{
    public Guid Id { get; set; }
    public Guid WorkDayId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool CountedInReports { get; set; } = true;
}

public class Report
{
    public Guid Id { get; set; }
    public Guid WorkDayId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class Comment
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid ManagerId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class Absence
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime Date { get; set; }
    public AbsenceReason Reason { get; set; }

    // BR25: the administrator or manager who set this status, so the employee can
    // be told in the BR27 tooltip whom to contact (BR30 author of the change).
    public Guid? CreatedByUserId { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PeriodClosure
{
    public Guid Id { get; set; }

    // BR23.1: the billing period belongs to one department, so the closure is
    // addressable by department and not only by its own Id (GetActiveForDepartmentAsync
    // can now answer honestly).
    public Guid DepartmentId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }

    // BR23.2: "Reopen closed period" requires a reason / act number, and the action
    // is logged as an "Emergency correction" by this administrator. Null while the
    // closure has never been reopened.
    public string? ReopenReason { get; set; }
    public Guid? ReopenedByUserId { get; set; }
}
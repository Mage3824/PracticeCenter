// Response ("wire") models of WorkTime.Api plus the scalar formatting decisions (Stage 2).
//
// Property names are exactly the JSON keys fixed by api-contracts.md, so MVC's JsonSerializer emits
// them verbatim. Every field already carries its contract format when it is filled by a converter
// below: Guid travels as canonical UUID text, date-only values as "YYYY-MM-DD", time-of-day values
// as "HH:mm", durations as integer minutes (the key names carry the Minutes suffix), timestamps as
// "YYYY-MM-DDTHH:mm:ss" without a timezone suffix, and enums as their C# enumerator name.
//
// A null field means "not applicable right now" (an open shift has no endTime, a login failure has
// no accessToken); the frontend treats null as "absent".
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using WorkTime.Domain;

namespace WorkTime.Api;

public class LoginWire
{
    public bool isSuccess { get; set; } = false;

    public bool requiresCaptcha { get; set; } = false;

    public bool isLocked { get; set; } = false;

    public string message { get; set; } = string.Empty;

    public string? lockedUntil { get; set; }

    // Stage-2 addition: present only when the login succeeded. api-contracts.md §7.3 keeps the
    // failure body free of these keys, so a null value is not written out at all.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? accessToken { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? expiresIn { get; set; }
}

public class WorkDayWire
{
    public Guid id { get; set; }

    public Guid userId { get; set; }

    public string date { get; set; } = string.Empty;

    public string startTime { get; set; } = string.Empty;

    public string? endTime { get; set; }

    public string status { get; set; } = string.Empty;

    public bool countedInReports { get; set; } = true;

    public bool isBreakLimitExceeded { get; set; } = false;
}

public class BreakWire
{
    public Guid id { get; set; }

    public Guid workDayId { get; set; }

    public string startTime { get; set; } = string.Empty;

    public string? endTime { get; set; }

    public long? durationMinutes { get; set; }

    public bool countedInReports { get; set; } = true;
}

public class ReportWire
{
    public Guid id { get; set; }

    public Guid workDayId { get; set; }

    public string text { get; set; } = string.Empty;

    public string createdAt { get; set; } = string.Empty;

    public string? updatedAt { get; set; }
}

public class DailyReportWire
{
    public string startTime { get; set; } = string.Empty;

    public string? endTime { get; set; }

    public List<ReportBreakWire> breaks { get; set; } = new();

    public long totalMinutes { get; set; } = 0;
}

public class ReportBreakWire
{
    public string startTime { get; set; } = string.Empty;

    public string? endTime { get; set; }
}

public class MonthlyReportWire
{
    public string employeeName { get; set; } = string.Empty;

    // Sparse: keyed by "YYYY-MM-DD", value = integer minutes (BR15/BR26/BR5 skip days).
    public Dictionary<string, long> hoursByDay { get; set; } = new();

    public long totalMinutes { get; set; } = 0;

    public List<string> absenceDays { get; set; } = new();
}

public class AbsenceWire
{
    public Guid id { get; set; }

    public Guid userId { get; set; }

    public string date { get; set; } = string.Empty;

    public string reason { get; set; } = string.Empty;

    public string? createdByUserId { get; set; }
}

public class AuditLogWire
{
    public Guid id { get; set; }

    public Guid userId { get; set; }

    public string action { get; set; } = string.Empty;

    public string entityType { get; set; } = string.Empty;

    public Guid entityId { get; set; }

    public string? oldValue { get; set; }

    public string? newValue { get; set; }

    public string timestamp { get; set; } = string.Empty;
}

// Scalar formatting decisions of §3 in api-contracts.md, kept in one place so every controller uses
// the same conversion. A nullable overload emits the "or null" half of a contract cell; the
// non-nullable one is used where the contract never allows null.
public class WireFormats
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss";

    // Date-only: midnight noise never crosses the wire (BR32/timezone policy is Stage 4).
    public static string TryDateText(DateTime date)
    {
        return date.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    // Server time, no timezone suffix.
    public static string TryTimestampText(DateTime timestamp)
    {
        return timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    // Server time, no timezone suffix. Null stays null.
    public static string? TryTimestampText(DateTime? timestamp)
    {
        if (timestamp == null)
        {
            return null;
        }

        return timestamp.Value.ToString(TimestampFormat, CultureInfo.InvariantCulture);
    }

    // Time-of-day: 24 h, minutes only - seconds never matter for a clock time.
    public static string TryTimeOfDayText(TimeSpan timeOfDay)
    {
        return PadTwo(timeOfDay.Hours) + ":" + PadTwo(timeOfDay.Minutes);
    }

    // Time-of-day: 24 h, minutes only. Null stays null - null means "still open" (BR2/BR10/BR15).
    public static string? TryTimeOfDayText(TimeSpan? timeOfDay)
    {
        if (timeOfDay == null)
        {
            return null;
        }

        return TryTimeOfDayText(timeOfDay.Value);
    }

    // Duration: integer minutes. Null means "still open".
    public static long? TryMinutesOfDuration(TimeSpan? duration)
    {
        if (duration == null)
        {
            return null;
        }

        return TryMinutesOfSpan(duration.Value);
    }

    // Duration of a value that is always present (a report total). TimeSpan counts whole ticks, so
    // rounding only absorbs the floating-point error of TotalMinutes, never a real fraction.
    public static long TryMinutesOfSpan(TimeSpan duration)
    {
        return (long)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero);
    }

    // Guid as canonical UUID text, or null for an unset reference.
    public static string? TryGuidText(Guid? id)
    {
        if (id == null || id.Value == Guid.Empty)
        {
            return null;
        }

        return id.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static string PadTwo(int value)
    {
        return value.ToString("D2", CultureInfo.InvariantCulture);
    }
}

// DTO -> wire conversions. Nothing here knows a repository or a business rule: it only renames and
// formats what the Application layer already decided.
public class WireConversion
{
    public static LoginWire TryFromAuthResult(AuthResult result, string? accessToken, long expiresIn)
    {
        var wire = new LoginWire();
        wire.isSuccess = result.IsSuccess;
        wire.requiresCaptcha = result.RequiresCaptcha;
        wire.isLocked = result.IsLocked;
        wire.message = result.Message;
        wire.lockedUntil = WireFormats.TryTimestampText(result.LockedUntil);

        if (result.IsSuccess)
        {
            wire.accessToken = accessToken;
            wire.expiresIn = expiresIn;
        }

        return wire;
    }

    public static WorkDayWire TryFromWorkDayDto(WorkDayDto dto)
    {
        var wire = new WorkDayWire();
        wire.id = dto.Id;
        wire.userId = dto.UserId;
        wire.date = WireFormats.TryDateText(dto.Date);
        wire.startTime = WireFormats.TryTimeOfDayText(dto.StartTime);
        wire.endTime = WireFormats.TryTimeOfDayText(dto.EndTime);
        wire.status = dto.Status.ToString();
        wire.countedInReports = dto.CountedInReports;
        wire.isBreakLimitExceeded = dto.IsBreakLimitExceeded;
        return wire;
    }

    // The services hand back domain entities, so the same fields are read straight off them - a DTO
    // exists for the frontend's view only.
    public static WorkDayWire TryFromWorkDay(WorkDay workDay)
    {
        var wire = new WorkDayWire();
        wire.id = workDay.Id;
        wire.userId = workDay.UserId;
        wire.date = WireFormats.TryDateText(workDay.Date);
        wire.startTime = WireFormats.TryTimeOfDayText(workDay.StartTime);
        wire.endTime = WireFormats.TryTimeOfDayText(workDay.EndTime);
        wire.status = workDay.Status.ToString();
        wire.countedInReports = workDay.CountedInReports;
        wire.isBreakLimitExceeded = workDay.IsBreakLimitExceeded;
        return wire;
    }

    public static BreakWire TryFromBreak(Break workBreak)
    {
        var wire = new BreakWire();
        wire.id = workBreak.Id;
        wire.workDayId = workBreak.WorkDayId;
        wire.startTime = WireFormats.TryTimeOfDayText(workBreak.StartTime);
        wire.endTime = WireFormats.TryTimeOfDayText(workBreak.EndTime);
        wire.durationMinutes = WireFormats.TryMinutesOfDuration(workBreak.Duration);
        wire.countedInReports = workBreak.CountedInReports;
        return wire;
    }

    public static ReportWire TryFromReport(Report report)
    {
        var wire = new ReportWire();
        wire.id = report.Id;
        wire.workDayId = report.WorkDayId;
        wire.text = report.Text;
        wire.createdAt = WireFormats.TryTimestampText(report.CreatedAt);
        wire.updatedAt = WireFormats.TryTimestampText(report.UpdatedAt);
        return wire;
    }

    public static AbsenceWire TryFromAbsence(Absence absence)
    {
        var wire = new AbsenceWire();
        wire.id = absence.Id;
        wire.userId = absence.UserId;
        wire.date = WireFormats.TryDateText(absence.Date);
        wire.reason = absence.Reason.ToString();
        wire.createdByUserId = WireFormats.TryGuidText(absence.CreatedByUserId);
        return wire;
    }

    public static AuditLogWire TryFromAuditLog(AuditLog auditLog)
    {
        var wire = new AuditLogWire();
        wire.id = auditLog.Id;
        wire.userId = auditLog.UserId;
        wire.action = auditLog.Action.ToString();
        wire.entityType = auditLog.EntityType;
        wire.entityId = auditLog.EntityId;
        wire.oldValue = auditLog.OldValue;
        wire.newValue = auditLog.NewValue;
        wire.timestamp = WireFormats.TryTimestampText(auditLog.Timestamp);
        return wire;
    }

    public static AuditLogWire[] TryFromAuditLogs(IEnumerable<AuditLog> logs)
    {
        var result = new List<AuditLogWire>();
        foreach (var log in logs)
        {
            result.Add(TryFromAuditLog(log));
        }

        return result.ToArray();
    }

    public static BreakWire TryFromBreakDto(BreakDto dto)
    {
        var wire = new BreakWire();
        wire.id = dto.Id;
        wire.workDayId = dto.WorkDayId;
        wire.startTime = WireFormats.TryTimeOfDayText(dto.StartTime);
        wire.endTime = WireFormats.TryTimeOfDayText(dto.EndTime);
        wire.durationMinutes = WireFormats.TryMinutesOfDuration(dto.Duration);
        wire.countedInReports = dto.CountedInReports;
        return wire;
    }

    public static ReportWire TryFromReportDto(ReportDto dto)
    {
        var wire = new ReportWire();
        wire.id = dto.Id;
        wire.workDayId = dto.WorkDayId;
        wire.text = dto.Text;
        wire.createdAt = WireFormats.TryTimestampText(dto.CreatedAt);
        wire.updatedAt = WireFormats.TryTimestampText(dto.UpdatedAt);
        return wire;
    }

    public static ReportWire[] TryFromReportDtos(IEnumerable<ReportDto> dtos)
    {
        var result = new List<ReportWire>();
        foreach (var dto in dtos)
        {
            result.Add(TryFromReportDto(dto));
        }

        return result.ToArray();
    }

    public static DailyReportWire TryFromDailyReport(DailyReport report)
    {
        var wire = new DailyReportWire();
        wire.startTime = WireFormats.TryTimeOfDayText(report.StartTime);
        wire.endTime = WireFormats.TryTimeOfDayText(report.EndTime);
        wire.totalMinutes = WireFormats.TryMinutesOfSpan(report.TotalHours);

        foreach (var reportBreak in report.Breaks)
        {
            var breakWire = new ReportBreakWire();
            breakWire.startTime = WireFormats.TryTimeOfDayText(reportBreak.StartTime);
            breakWire.endTime = WireFormats.TryTimeOfDayText(reportBreak.EndTime);
            wire.breaks.Add(breakWire);
        }

        return wire;
    }

    public static MonthlyReportWire[] TryFromMonthlyReports(IEnumerable<MonthlyReport> reports)
    {
        var result = new List<MonthlyReportWire>();

        foreach (var report in reports)
        {
            var wire = new MonthlyReportWire();
            wire.employeeName = report.EmployeeName;
            wire.totalMinutes = WireFormats.TryMinutesOfSpan(report.TotalHours);

            foreach (var entry in report.HoursByDay)
            {
                wire.hoursByDay[WireFormats.TryDateText(entry.Key)] = WireFormats.TryMinutesOfSpan(entry.Value);
            }

            foreach (var day in report.AbsenceDays)
            {
                wire.absenceDays.Add(WireFormats.TryDateText(day));
            }

            result.Add(wire);
        }

        return result.ToArray();
    }

    public static AbsenceWire TryFromAbsenceDto(AbsenceDto dto)
    {
        var wire = new AbsenceWire();
        wire.id = dto.Id;
        wire.userId = dto.UserId;
        wire.date = WireFormats.TryDateText(dto.Date);
        wire.reason = dto.Reason.ToString();
        wire.createdByUserId = WireFormats.TryGuidText(dto.CreatedByUserId);
        return wire;
    }

    public static AbsenceWire[] TryFromAbsenceDtos(IEnumerable<AbsenceDto> dtos)
    {
        var result = new List<AbsenceWire>();
        foreach (var dto in dtos)
        {
            result.Add(TryFromAbsenceDto(dto));
        }

        return result.ToArray();
    }

    public static AuditLogWire TryFromAuditLogDto(AuditLogDto dto)
    {
        var wire = new AuditLogWire();
        wire.id = dto.Id;
        wire.userId = dto.UserId;
        wire.action = dto.Action.ToString();
        wire.entityType = dto.EntityType;
        wire.entityId = dto.EntityId;
        wire.oldValue = dto.OldValue;
        wire.newValue = dto.NewValue;
        wire.timestamp = WireFormats.TryTimestampText(dto.Timestamp);
        return wire;
    }

    public static AuditLogWire[] TryFromAuditLogDtos(IEnumerable<AuditLogDto> dtos)
    {
        var result = new List<AuditLogWire>();
        foreach (var dto in dtos)
        {
            result.Add(TryFromAuditLogDto(dto));
        }

        return result.ToArray();
    }
}

// envelope error{code,message,httpStatus} строится в одном месте — ErrorEnvelope.TryEnvelope
// (Middleware.cs); текстовые builder'ы здесь были мёртвым кодом и удалены.

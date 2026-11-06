// Application-layer DTOs for report rendering (no repository/service logic here).

public class DailyReport
{
    public TimeSpan StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    // Список перерывов, а не словарь
    public List<ReportBreak> Breaks { get; set; } = new();

    public TimeSpan TotalHours { get; set; }
}

public class ReportBreak
{
    public TimeSpan StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }
}

public class MonthlyReport
{
    public string EmployeeName { get; set; } = string.Empty;

    // Инициализация словаря
    public Dictionary<DateTime, TimeSpan> HoursByDay { get; set; } = new();

    public TimeSpan TotalHours { get; set; }

    // BR26: days that carry an "Absence" status are never counted as hours; the
    // frontend renders them as their own column. Calendar dates only (time part is zero).
    public List<DateTime> AbsenceDays { get; set; } = new();
}
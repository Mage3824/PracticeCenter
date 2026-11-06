
public class ReportSearchItem
{
    public Guid Id { get; set; }

    public Guid WorkDayId { get; set; }

    public Guid EmployeeId { get; set; }

    public string EmployeeName { get; set; } = string.Empty;

    public Guid DepartmentId { get; set; }

    public DateTime Date { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

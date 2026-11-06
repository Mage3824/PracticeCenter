// Application-layer DTO for handing a text report to the frontend (BR14 daily report,
// BR12 editing window).
public class ReportDto
{
    public Guid Id { get; set; }

    // The finished working day this report describes.
    public Guid WorkDayId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // BR12/BR30: set only after an edit (by the employee before his day ends, or by
    // the administrator afterwards).
    public DateTime? UpdatedAt { get; set; }
}

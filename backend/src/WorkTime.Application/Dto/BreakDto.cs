// Application-layer DTO for handing a Break to the frontend (BR14 lists the breaks of
// a day with their start and end times).
public class BreakDto
{
    public Guid Id { get; set; }

    // The working day this break belongs to (BR6).
    public Guid WorkDayId { get; set; }

    public TimeSpan StartTime { get; set; }

    // Null while the break is still open (BR10: at most one open break per employee).
    public TimeSpan? EndTime { get; set; }

    // Null while the break is still open.
    public TimeSpan? Duration { get; set; }

    // BR8: false for a break shorter than 5 minutes - it is neither subtracted from
    // worked time nor saved in reports.
    public bool CountedInReports { get; set; } = true;
}

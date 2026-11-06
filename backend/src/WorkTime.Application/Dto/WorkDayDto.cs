// Application-layer DTO for handing a WorkDay to the frontend. A flat mirror of the
// domain entity: Guid ids travel as their string form, TimeSpan / DateTime as plain
// scalars, so no repository or service logic is reachable through this type.
public class WorkDayDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateTime Date { get; set; }

    public TimeSpan StartTime { get; set; }

    // Null while the day is still open (BR2/BR15).
    public TimeSpan? EndTime { get; set; }

    public WorkDayStatus Status { get; set; }

    // BR5: false when less than 15 minutes were worked, so the day is absent from
    // the monthly report.
    public bool CountedInReports { get; set; } = true;

    // BR9: total break time of this day passed the 4 hour cap - the manager is
    // notified through this flag instead of a rejected action.
    public bool IsBreakLimitExceeded { get; set; }
}

// Application-layer DTO for handing the billing-period closure state to the frontend
// (BR23.1 closing of the period, BR23.2 reopening by the administrator).
public class PeriodClosureDto
{
    public Guid Id { get; set; }

    // BR23.1: the department whose billing period this closure stands for.
    public Guid DepartmentId { get; set; }

    // The calendar month this closure stands for (BR16/BR18 report window).
    public int Year { get; set; }

    public int Month { get; set; }

    public bool IsClosed { get; set; }

    public DateTime? ClosedAt { get; set; }

    // The administrator who closed the period.
    public Guid? ClosedBy { get; set; }

    // BR23.2: the mandatory reason / act number given when a closed period was
    // reopened. Null while the period has never been reopened.
    public string? ReopenReason { get; set; }

    // BR23.2: the administrator who reopened the period ("Emergency correction").
    public Guid? ReopenedByUserId { get; set; }
}

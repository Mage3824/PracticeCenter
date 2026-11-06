// Application-layer DTO for handing an Absence status to the frontend (BR25, BR26, BR27).
public class AbsenceDto
{
    public Guid Id { get; set; }

    // The employee the status belongs to.
    public Guid UserId { get; set; }

    public DateTime Date { get; set; }

    public AbsenceReason Reason { get; set; }

    // BR25: the administrator or manager who set this status, so the BR27 tooltip can
    // tell the employee whom to contact. Null when the record predates that bookkeeping.
    public Guid? CreatedByUserId { get; set; }
}

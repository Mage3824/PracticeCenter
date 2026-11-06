// Application-layer DTO for handing a manager comment on a report to the frontend.
// BR13: comments are added by a manager and visible only to the manager and the
// administrator, so this DTO is never sent to an employee row of the UI.
public class CommentDto
{
    public Guid Id { get; set; }

    // The report being commented on.
    public Guid ReportId { get; set; }

    // The manager who wrote the comment.
    public Guid ManagerId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

// Application-layer DTO for handing an event-log entry to the frontend. BR29: logs are
// visible only to the administrator; BR30: a change carries the previous value, the new
// value, the author and the time.
public class AuditLogDto
{
    public Guid Id { get; set; }

    // Who performed the action.
    public Guid UserId { get; set; }

    public AuditAction Action { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime Timestamp { get; set; }
}

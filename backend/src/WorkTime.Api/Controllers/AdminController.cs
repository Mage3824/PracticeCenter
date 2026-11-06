// Endpoint 15 of api-contracts.md (the path this Stage gives it: GET /api/audit-logs). BR29 makes the
// event log readable by the administrator only, which is exactly what the AdminOnly policy enforces;
// BR30 needs the previous value, the author and the time, and AuditLogDto already carries all three.
//
// The read path exists in the repository layer only (no service writes logs besides the BR3
// auto-finish entry today), so this controller reads through IAuditLogRepository directly - still
// only through an interface, never a concrete repository type.
using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;
using Microsoft.AspNetCore.Mvc;

namespace WorkTime.Api;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly IAuditLogRepository _auditLogs;

    private readonly IPeriodClosureRepository _periodClosures;

    private readonly IUserRepository _users;

    private readonly RequestContextProvider _requestContext;

    public AdminController(
        IAuditLogRepository auditLogs,
        IPeriodClosureRepository periodClosures,
        IUserRepository users,
        RequestContextProvider requestContext)
    {
        _auditLogs = auditLogs;
        _periodClosures = periodClosures;
        _users = users;
        _requestContext = requestContext;
    }

    // entityId is required: the log is read per entity (GetHistoryForEntityAsync), and the client sends
    // that criterion rather than a list of entries.
    [HttpGet("/api/audit-logs")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuditLogWire[]))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<AuditLogWire[]> TryGetAuditHistory(Guid entityId)
    {
        var cancellationToken = _requestContext.TryStopSignal();

        if (entityId == Guid.Empty)
        {
            throw new InvalidRequestException(
                "ENTITY_ID_MISSING",
                "[RU] Query parameter 'entityId' is required.");
        }

        var history = await _auditLogs.GetHistoryForEntityAsync(entityId, cancellationToken);
        return WireConversion.TryFromAuditLogs(history);
    }

    // BR23.1: закрытие расчётного периода настраивается администратором; с этого момента
    // редактирование данных внутри периода блокируется (BR12 в сервисе отчётов).
    [HttpPost("/api/period-closures/{periodClosureId}/close")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PeriodClosureWire))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<PeriodClosureWire> TryClosePeriod(Guid periodClosureId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var closure = await _periodClosures.GetByIdAsync(periodClosureId, cancellationToken);
        if (closure == null)
        {
            throw new EntityNotFoundException(periodClosureId, "PeriodClosure");
        }

        if (closure.IsClosed)
        {
            throw new BusinessRuleException("BR23.1", "[RU] This billing period is already closed.");
        }

        closure.IsClosed = true;
        closure.ClosedAt = DateTime.UtcNow;
        closure.ClosedBy = actorId;

        await _periodClosures.SaveAsync(closure, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Update, "PeriodClosure", periodClosureId, "isClosed=false",
            "isClosed=true (BR23.1)", cancellationToken);

        return ManagementWireConversion.TryFromPeriodClosure(closure);
    }

    // BR23.2: вскрытие закрытого периода требует причину / номер акта и логируется как
    // «Экстренная корректировка».
    [HttpPost("/api/period-closures/{periodClosureId}/reopen")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PeriodClosureWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<PeriodClosureWire> TryReopenPeriod(Guid periodClosureId, ReopenPeriodRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var reason = RequestParsers.TryRequireText(payload.reason, "reason");

        var closure = await _periodClosures.GetByIdAsync(periodClosureId, cancellationToken);
        if (closure == null)
        {
            throw new EntityNotFoundException(periodClosureId, "PeriodClosure");
        }

        if (!closure.IsClosed)
        {
            throw new BusinessRuleException("BR23.2", "[RU] Only a closed billing period can be reopened.");
        }

        closure.IsClosed = false;
        closure.ReopenReason = reason;
        closure.ReopenedByUserId = actorId;

        await _periodClosures.SaveAsync(closure, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Update, "PeriodClosure", periodClosureId, "isClosed=true",
            "[RU] Emergency correction: " + reason, cancellationToken);

        return ManagementWireConversion.TryFromPeriodClosure(closure);
    }
}

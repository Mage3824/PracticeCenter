// Endpoints 5 and 6 of api-contracts.md: the breaks of a working day.
//
// The break start is addressed by the working day it belongs to (BR6), so its route keeps the
// work-day prefix even though the entity is a Break; finishing one only needs the break Id, which
// resolves its owner inside the service through Break.WorkDayId.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WorkTime.Api;

[ApiController]
[Route("api")]
// Все аутентифицированные роли (BR19): владение днём проверяет сервис (BR21 -> 409).
[Authorize]
public class BreaksController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly BreakService _breakService;

    private readonly IUserRepository _users;

    private readonly RequestContextProvider _requestContext;

    public BreaksController(
        BreakService breakService,
        IUserRepository users,
        RequestContextProvider requestContext)
    {
        _breakService = breakService;
        _users = users;
        _requestContext = requestContext;
    }

    [HttpPost("work-days/{workDayId}/breaks/start")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BreakWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<BreakWire> TryStartBreak(Guid workDayId, StartBreakRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var employeeId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, employeeId, cancellationToken);

        var startTime = RequestParsers.TryParseTimeOfDayText(payload.startTime, "startTime");

        // BR9 is not an error here: exceeding the 4 hour break cap sets isBreakLimitExceeded on the
        // day instead of rejecting the request.
        var workBreak = await _breakService.StartBreakAsync(employeeId, workDayId, startTime, cancellationToken);
        return WireConversion.TryFromBreak(workBreak);
    }

    [HttpPost("breaks/{breakId}/finish")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BreakWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<BreakWire> TryFinishBreak(Guid breakId, FinishBreakRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var employeeId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, employeeId, cancellationToken);

        var endTime = RequestParsers.TryParseTimeOfDayText(payload.endTime, "endTime");

        // A break shorter than 5 minutes is silent (BR8): it finishes normally and comes back with
        // countedInReports = false.
        var workBreak = await _breakService.FinishBreakAsync(breakId, endTime, employeeId, cancellationToken);
        return WireConversion.TryFromBreak(workBreak);
    }
}

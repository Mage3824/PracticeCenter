// Endpoints 2 and 3 of api-contracts.md plus the "current shift" read the frontend needs to reopen
// its work day after a reload.
//
// The employee is always the token subject; date / startTime / endTime travel as contract scalars.
// Business-rule rejections (BR1 second open day, BR2 day not open, BR21 foreign day) are thrown by
// the services and turned into 409/404 envelopes by WorkTimeExceptionHandler.
using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;
using Microsoft.AspNetCore.Mvc;

namespace WorkTime.Api;

[ApiController]
[Route("api/work-days")]
// Все аутентифицированные роли (BR19, BR3): владение днём проверяет сервис (BR21 -> 409).
[Authorize]
public class WorkDaysController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly WorkTimeService _workTimeService;

    private readonly IUserRepository _users;

    // Only used to read the open day of the caller (GET current); business rules stay in the service.
    private readonly IWorkDayRepository _workDays;
    private readonly AbsenceService _absenceService;

    private readonly RequestContextProvider _requestContext;

    public WorkDaysController(
        WorkTimeService workTimeService,
        IUserRepository users,
        IWorkDayRepository workDays,
        AbsenceService absenceService,
        RequestContextProvider requestContext)
    {
        _workTimeService = workTimeService;
        _users = users;
        _workDays = workDays;
        _absenceService = absenceService;
        _requestContext = requestContext;
    }

    [HttpPost("start")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkDayWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<WorkDayWire> TryStartWorkDay(StartWorkDayRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var employeeId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        // Stage-2 identity gap: the service does not resolve the user, so an unknown subject would
        // otherwise create a day for nobody.
        await RequestIdentity.TryRequireKnownUserAsync(_users, employeeId, cancellationToken);

        var date = RequestParsers.TryParseDateText(payload.date, "date");
        var startTime = RequestParsers.TryParseTimeOfDayText(payload.startTime, "startTime");

        var workDay = await _workTimeService.StartWorkDayAsync(employeeId, date, startTime, cancellationToken);
        return WireConversion.TryFromWorkDay(workDay);
    }

    [HttpPost("{workDayId}/finish")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkDayWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<WorkDayWire> TryFinishWorkDay(Guid workDayId, FinishWorkDayRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var employeeId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, employeeId, cancellationToken);

        var endTime = RequestParsers.TryParseTimeOfDayText(payload.endTime, "endTime");

        // BR7 side effect (an open break is auto-closed with the day) lives in the service.
        var workDay = await _workTimeService.FinishWorkDayAsync(employeeId, workDayId, endTime, cancellationToken);
        return WireConversion.TryFromWorkDay(workDay);
    }

    [HttpGet("current")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkDayWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TryGetCurrentWorkDay()
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var employeeId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, employeeId, cancellationToken);

        // "No open shift" is not a 404: the contract answers with a null body (200), because the
        // frontend distinguishes "nothing to show" from "an error happened".
        var openDays = await _workDays.GetActiveForEmployeeOnDateAsync(employeeId, DateTime.UtcNow, cancellationToken);

        foreach (var entry in openDays)
        {
            return Ok(WireConversion.TryFromWorkDay(entry.Value));
        }

        // Значение null в MVC даёт 204 No Content, а §2 требует 200. Открытой смены нет ->
        // 200 OK с телом literal "null" и content-type application/json (как в ReportsController):
        // контракт §2 объявляет WorkDayWire или null, пустое тело контракту противоречит.
        return new ContentResult
        {
            ContentType = "application/json",
            StatusCode = StatusCodes.Status200OK,
            Content = "null",
        };
    }

    // BR3: аварийное авто-завершение смены на 23:59:59 (запись в журнал делает сервис).
    [HttpPost("{workDayId}/auto-finish")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkDayWire))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<WorkDayWire> TryAutoFinishWorkDay(Guid workDayId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var employeeId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, employeeId, cancellationToken);

        var workDay = await _workTimeService.AutoFinishWorkDayAsync(employeeId, workDayId, cancellationToken);
        return WireConversion.TryFromWorkDay(workDay);
    }

    // BR27: состояние кнопки «Начать работу». BR21 — сотрудник спрашивает только про себя.
    [HttpGet("{employeeId}/can-start")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CanStartWorkDayWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<CanStartWorkDayWire> TryCanStartWorkDay(Guid employeeId, [FromQuery] string? date)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var callerId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, callerId, cancellationToken);

        if (employeeId != callerId)
        {
            throw new BusinessRuleException("BR21", "[RU] An employee may check only their own work day.");
        }

        var day = RequestParsers.TryParseDateText(date, "date");
        var canStart = await _absenceService.CanStartWorkDayAsync(employeeId, day, cancellationToken);

        return new CanStartWorkDayWire { canStart = canStart };
    }
}

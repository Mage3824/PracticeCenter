// Endpoint 11 of api-contracts.md: an administrator or manager sets an "Absence" status for an
// employee (BR25 reason, BR28 one record per day reusing its id, BR27 the tooltip that names who set
// it). The author is the token subject; only the target employee and the date/reason travel in the
// request. BR25/BR28 role gates inside the service arrive as 409, a missing actor or target as 404.
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;
using Microsoft.AspNetCore.Mvc;

namespace WorkTime.Api;

[ApiController]
[Route("api/absences")]
// Все аутентифицированные роли: управленческие пути закрыты политикой на методах,
// сотрудник читает только собственный статус (BR27).
[Authorize]
public class AbsencesController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly AbsenceService _absenceService;

    private readonly IUserRepository _users;
    private readonly IAbsenceRepository _absences;

    private readonly RequestContextProvider _requestContext;

    public AbsencesController(
        AbsenceService absenceService,
        IUserRepository users,
        IAbsenceRepository absences,
        RequestContextProvider requestContext)
    {
        _absenceService = absenceService;
        _users = users;
        _absences = absences;
        _requestContext = requestContext;
    }

    [HttpPost("{employeeId}")]
    [Authorize(Policy = "ManagerOrAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AbsenceWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<AbsenceWire> TrySetAbsence(Guid employeeId, SetAbsenceRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var date = RequestParsers.TryParseDateText(payload.date, "date");
        var reason = RequestParsers.TryParseAbsenceReason(payload.reason);

        // BR28: setting a status for a day that already has one reuses the existing record's id.
        var absence = await _absenceService.SetAbsenceAsync(actorId, employeeId, date, reason, cancellationToken);
        return WireConversion.TryFromAbsence(absence);
    }

    // PUT — та же семантика, что и POST: BR28 переиспользует id уже существующей записи за день.
    [HttpPut("{employeeId}")]
    [Authorize(Policy = "ManagerOrAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AbsenceWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<AbsenceWire> TryUpdateAbsence(Guid employeeId, SetAbsenceRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var date = RequestParsers.TryParseDateText(payload.date, "date");
        var reason = RequestParsers.TryParseAbsenceReason(payload.reason);

        var absence = await _absenceService.SetAbsenceAsync(actorId, employeeId, date, reason, cancellationToken);
        return WireConversion.TryFromAbsence(absence);
    }

    // Чтение отсутствия за день (BR27 tooltip: кто поставил статус — createdByUserId).
    [HttpGet("{employeeId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AbsenceWire[]))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TryGetAbsencesForDate(Guid employeeId, [FromQuery] string? date)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var day = RequestParsers.TryParseDateText(date, "date");

        // BR27: сотрудник читает статус по своему employeeId; чужой id -> 403.
        var subjectId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        if (!User.IsInRole("Manager") && !User.IsInRole("Admin") && employeeId != subjectId)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var found = await _absences.GetForEmployeeOnDateAsync(employeeId, day, cancellationToken);
        return Ok(TryFromAbsenceEntities(found));
    }

    [HttpGet("")]
    [Authorize(Policy = "ManagerOrAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AbsenceWire[]))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<AbsenceWire[]> TryGetAbsencesInRange([FromQuery] string? from, [FromQuery] string? to)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var start = RequestParsers.TryParseDateText(from, "from");
        var end = RequestParsers.TryParseDateText(to, "to");

        var found = await _absences.GetByDateRangeAsync(start, end, cancellationToken);
        return TryFromAbsenceEntities(found);
    }

    private static AbsenceWire[] TryFromAbsenceEntities(IEnumerable<Absence> found)
    {
        var result = new List<AbsenceWire>();

        foreach (var absence in found)
        {
            result.Add(WireConversion.TryFromAbsence(absence));
        }

        return result.ToArray();
    }
}

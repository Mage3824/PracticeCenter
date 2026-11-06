$ErrorActionPreference = 'Stop'
$enc = New-Object System.Text.UTF8Encoding($false)

function ReplaceOnce([string]$path, [string]$old, [string]$new) {
    $raw = [System.IO.File]::ReadAllText($path)
    $c = $raw.Replace("`r`n", "`n")
    if (-not $c.Contains($old)) { throw "anchor missing in $path" }
    $c = $c.Replace($old, $new)
    [System.IO.File]::WriteAllText($path, $c, $enc)
}

$wd = 'src/WorkTime.Api/Controllers/WorkDaysController.cs'
ReplaceOnce $wd 'using Microsoft.AspNetCore.Authorization;' 'using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;'
ReplaceOnce $wd '    private readonly IWorkDayRepository _workDays;' '    private readonly IWorkDayRepository _workDays;
    private readonly AbsenceService _absenceService;'

ReplaceOnce $wd @'
        IWorkDayRepository workDays,
        RequestContextProvider requestContext)
    {
        _workTimeService = workTimeService;
        _users = users;
        _workDays = workDays;
        _requestContext = requestContext;
    }
'@ @'
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
'@

ReplaceOnce $wd @'
        return null;
    }
}
'@ @'
        return null;
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
'@

$rp = 'src/WorkTime.Api/Controllers/ReportsController.cs'
ReplaceOnce $rp 'using Microsoft.AspNetCore.Authorization;' 'using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;'

ReplaceOnce $rp @'
[Route("api/reports")]
public class ReportsController : Microsoft.AspNetCore.Mvc.ControllerBase
'@ @'
[Route("api/reports")]
[Authorize]
public class ReportsController : Microsoft.AspNetCore.Mvc.ControllerBase
'@

ReplaceOnce $rp @'
    private readonly RequestContextProvider _requestContext;

    public ReportsController(
        ReportService reportService,
        IUserRepository users,
        RequestContextProvider requestContext)
    {
        _reportService = reportService;
        _users = users;
        _requestContext = requestContext;
    }
'@ @'
    private readonly IReportRepository _reports;

    private readonly ICommentRepository _comments;

    private readonly RequestContextProvider _requestContext;

    public ReportsController(
        ReportService reportService,
        IUserRepository users,
        IReportRepository reports,
        ICommentRepository comments,
        RequestContextProvider requestContext)
    {
        _reportService = reportService;
        _users = users;
        _reports = reports;
        _comments = comments;
        _requestContext = requestContext;
    }
'@

ReplaceOnce $rp @'
    [HttpGet("monthly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MonthlyReportWire))]
'@ @'
    // BR19: месячный сводка и экспорт — руководитель и администратор.
    [HttpGet("monthly")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MonthlyReportWire))]
'@

ReplaceOnce $rp @'
        var report = await _reportService.CreateOrUpdateReportAsync(editorId, workDayId, payload.text, cancellationToken);
        return WireConversion.TryFromReport(report);
    }
}
'@ @'
        var report = await _reportService.CreateOrUpdateReportAsync(editorId, workDayId, payload.text, cancellationToken);
        return WireConversion.TryFromReport(report);
    }

    // BR13: комментарий руководителя к отчёту, видимый руководителю и администратору.
    [HttpPost("{workDayId}/comments")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CommentWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<CommentWire> TryAddComment(Guid workDayId, CreateCommentRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var managerId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, managerId, cancellationToken);

        var reportId = payload.reportId ?? throw new InvalidRequestException(
            "REPORT_ID_REQUIRED",
            "[RU] Field 'reportId' is required.");

        var report = await _reports.GetByIdAsync(reportId, cancellationToken);
        if (report == null)
        {
            throw new EntityNotFoundException(reportId, "Report");
        }

        if (report.WorkDayId != workDayId)
        {
            throw new InvalidRequestException("REPORT_WORK_DAY_MISMATCH", "[RU] This report belongs to another work day.");
        }

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            ReportId = reportId,
            ManagerId = managerId,
            Text = RequestParsers.TryRequireText(payload.text, "text"),
            CreatedAt = DateTime.UtcNow,
        };

        await _comments.SaveAsync(comment, cancellationToken);
        return ManagementWireConversion.TryFromComment(comment);
    }

    [HttpGet("{workDayId}/comments")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CommentWire[]))]
    public async Task<CommentWire[]> TryListComments(Guid workDayId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var comments = await _comments.GetAllForWorkDayAsync(workDayId, cancellationToken);
        return ManagementWireConversion.TryFromComments(comments.Values);
    }

    // BR17: экспорт — руководитель и администратор. Excel появляется на этапе 4, поэтому сейчас CSV.
    [HttpGet("monthly/export")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(byte[]))]
    public async Task<IActionResult> TryExportMonthlyReportCsv(Guid? periodClosureId, Guid? departmentId)
    {
        var cancellationToken = _requestContext.TryStopSignal();

        var reports = WireConversion.TryFromMonthlyReports(
            await _reportService.GetMonthlyReportAsync(
                periodClosureId ?? Guid.Empty,
                departmentId ?? Guid.Empty,
                cancellationToken));

        var body = MonthlyReportCsv.TryBuild(reports);
        return new FileContentResult(body, "text/csv; charset=utf-8") { FileName = "monthly-report.csv" };
    }

    // Контрактный путь /export.xlsx: до этапа 4 отдаёт 501 вместо молча пустого файла.
    [HttpGet("monthly/export.xlsx")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult TryExportMonthlyReportExcel()
    {
        var problem = new ProblemDetails
        {
            Type = "https://worktime/api/errors/export-not-implemented",
            Title = "[RU] Excel export is not implemented yet (BR17, Stage 4).",
            Status = StatusCodes.Status501NotImplemented,
            Detail = "[RU] Use GET /api/reports/monthly/export for the CSV form of the same data.",
        };

        problem.Extensions["code"] = "EXPORT_NOT_IMPLEMENTED";
        problem.Extensions["httpStatus"] = StatusCodes.Status501NotImplemented;

        return StatusCode(StatusCodes.Status501NotImplemented, problem);
    }
}
'@

$ab = 'src/WorkTime.Api/Controllers/AbsencesController.cs'
ReplaceOnce $ab 'using Microsoft.AspNetCore.Authorization;' 'using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;'
ReplaceOnce $ab '    private readonly IUserRepository _users;' '    private readonly IUserRepository _users;
    private readonly IAbsenceRepository _absences;'

ReplaceOnce $ab @'
        IUserRepository users,
        RequestContextProvider requestContext)
    {
        _absenceService = absenceService;
        _users = users;
        _requestContext = requestContext;
    }
'@ @'
        IUserRepository users,
        IAbsenceRepository absences,
        RequestContextProvider requestContext)
    {
        _absenceService = absenceService;
        _users = users;
        _absences = absences;
        _requestContext = requestContext;
    }
'@

ReplaceOnce $ab @'
        var absence = await _absenceService.SetAbsenceAsync(actorId, employeeId, date, reason, cancellationToken);
        return WireConversion.TryFromAbsence(absence);
    }
}
'@ @'
        var absence = await _absenceService.SetAbsenceAsync(actorId, employeeId, date, reason, cancellationToken);
        return WireConversion.TryFromAbsence(absence);
    }

    // PUT — та же семантика, что и POST: BR28 переиспользует id уже существующей записи за день.
    [HttpPut("{employeeId}")]
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
    public async Task<AbsenceWire[]> TryGetAbsencesForDate(Guid employeeId, [FromQuery] string? date)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var day = RequestParsers.TryParseDateText(date, "date");

        var found = await _absences.GetForEmployeeOnDateAsync(employeeId, day, cancellationToken);
        return TryFromAbsenceEntities(found);
    }

    [HttpGet("")]
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
'@

$ad = 'src/WorkTime.Api/Controllers/AdminController.cs'
ReplaceOnce $ad 'using Microsoft.AspNetCore.Authorization;' 'using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;'

ReplaceOnce $ad @'
    private readonly RequestContextProvider _requestContext;

    public AdminController(IAuditLogRepository auditLogs, RequestContextProvider requestContext)
    {
        _auditLogs = auditLogs;
        _requestContext = requestContext;
    }
'@ @'
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
'@

ReplaceOnce $ad @'
        var history = await _auditLogs.GetHistoryForEntityAsync(entityId, cancellationToken);
        return WireConversion.TryFromAuditLogs(history);
    }
}
'@ @'
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
'@

Write-Output 'controllers appended'

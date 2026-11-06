// Endpoints 7, 8 and 9 of api-contracts.md: the text report of a day, the rendered daily report and
// the monthly table.
//
// The reader/editor is always the token subject (currentUserId / editorId never appear in a body).
// BR21 ownership and BR14/BR12 editing windows are enforced by ReportService and arrive as 409; a
// missing WorkDay or User arrives as 404. A day that has no report to render yet is a null body, not
// a 404 (the contract's "no report exists" answer).
using Microsoft.AspNetCore.Authorization;
using WorkTime.Domain;
using Microsoft.AspNetCore.Mvc;

namespace WorkTime.Api;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    private readonly ReportService _reportService;

    private readonly IUserRepository _users;

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

    // Endpoint 8. Nullable day (BR15) -> null body with 200.
    [HttpGet("daily/{workDayId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DailyReportWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TryGetDailyReport(Guid workDayId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var currentUserId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, currentUserId, cancellationToken);

        var report = await _reportService.GetDailyReportAsync(currentUserId, workDayId, cancellationToken);

        if (report == null)
        {
            // BR15: незавершённый день -> JSON null (контракт §1), а не пустое тело.
            return new ContentResult
            {
                ContentType = "application/json",
                StatusCode = StatusCodes.Status200OK,
                Content = "null",
            };
        }

        return Ok(WireConversion.TryFromDailyReport(report));
    }

    // Endpoint 9. The window comes from a closure or a department - never from a list of ids
    // (api-contracts.md §6). With neither criterion the service answers [] because there is no window.
    // BR19: месячный сводка и экспорт — руководитель и администратор.
    [HttpGet("monthly")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(MonthlyReportWire))]
    public async Task<MonthlyReportWire[]> TryGetMonthlyReport(Guid? periodClosureId, Guid? departmentId)
    {
        var cancellationToken = _requestContext.TryStopSignal();

        var closureId = periodClosureId ?? Guid.Empty;
        var idOfDepartment = departmentId ?? Guid.Empty;

        var reports = await _reportService.GetMonthlyReportAsync(closureId, idOfDepartment, cancellationToken);
        return WireConversion.TryFromMonthlyReports(reports);
    }

    // Endpoint 7: create or update the text report of a finished day (BR14 one report per day, BR12
    // editing window - only the administrator may edit after the day ended).
    [HttpPost("{workDayId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ReportWire> TryCreateOrUpdateReport(Guid workDayId, CreateReportRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var editorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());

        await RequestIdentity.TryRequireKnownUserAsync(_users, editorId, cancellationToken);

        // The report text may legitimately be empty (an employee clearing a draft), so only the
        // presence of the field is checked here.
        var report = await _reportService.CreateOrUpdateReportAsync(editorId, workDayId, payload.text!, cancellationToken);
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

        HttpContext.Response.ContentType = "text/csv; charset=utf-8";
        HttpContext.Response.Headers.Append("Content-Disposition", "attachment; filename=\"monthly-report.csv\"");
        await HttpContext.Response.Body.WriteAsync(body, 0, body.Length);

        return new StatusCodeResult(StatusCodes.Status200OK);
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

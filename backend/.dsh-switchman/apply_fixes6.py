# -*- coding: utf-8 -*-
# Устранение warnings (nullable) и оставшихся ошибок, формы ответов не меняются.
import sys

ROOT = r"E:\AItest\progcentr\project\backend"

def patch(rel, pairs):
    path = ROOT + "\\src\\" + rel
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        text = f.read()
    nl = "\r\n" if "\r\n" in text else "\n"
    work = text.replace("\r\n", "\n")
    for old, new, n in pairs:
        c = work.count(old)
        if c != n:
            print("FAIL %s: count=%d expected=%d for %r" % (rel, c, n, old[:44]))
            sys.exit(1)
        work = work.replace(old, new)
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(work.replace("\n", nl))
    print("OK %s (%d)" % (rel, len(pairs)))

# Program.cs: foreach по ModelErrorCollection — лишний .Errors
patch("WorkTime.Api\\Program.cs", [
    ("                foreach (var error in entryErrors.Errors)",
     "                foreach (var error in entryErrors)", 1),
])

# AbsencesController: GET собственного статуса -> IActionResult (форма 200 не меняется,
# 403 для чужого employeeId — BR27/BR21).
patch("WorkTime.Api\\Controllers\\AbsencesController.cs", [
    ("    public async Task<AbsenceWire[]> TryGetAbsencesForDate(Guid employeeId, [FromQuery] string? date)",
     "    public async Task<IActionResult> TryGetAbsencesForDate(Guid employeeId, [FromQuery] string? date)", 1),
    ("        var found = await _absences.GetForEmployeeOnDateAsync(employeeId, day, cancellationToken);\n        return TryFromAbsenceEntities(found);",
     "        var found = await _absences.GetForEmployeeOnDateAsync(employeeId, day, cancellationToken);\n        return Ok(TryFromAbsenceEntities(found));", 1),
])

# Nullable warnings: только аннотации/фorgiving, поведение то же (TryRequireText уже
# отверг null/пустстроку до вызова).
patch("WorkTime.Api\\Controllers\\AuthController.cs", [
    ("        var result = await _authService.LoginAsync(payload.login, payload.password, cancellationToken);",
     "        var result = await _authService.LoginAsync(payload.login!, payload.password!, cancellationToken);", 1),
])
patch("WorkTime.Api\\Controllers\\ReportsController.cs", [
    ("        var report = await _reportService.CreateOrUpdateReportAsync(editorId, workDayId, payload.text, cancellationToken);",
     "        var report = await _reportService.CreateOrUpdateReportAsync(editorId, workDayId, payload.text!, cancellationToken);", 1),
])
patch("WorkTime.Api\\Controllers\\WorkDaysController.cs", [
    ("    public async Task<WorkDayWire> TryGetCurrentWorkDay()",
     "    public async Task<WorkDayWire?> TryGetCurrentWorkDay()", 1),
])
patch("WorkTime.Application\\Services\\WorkTimeService.cs", [
    ("            previousEndTime = workDay.EndTime == null ? string.Empty : workDay.EndTime.ToString();",
     "            previousEndTime = workDay.EndTime == null ? string.Empty : workDay.EndTime.Value.ToString();", 1),
])
patch("src\\WorkTime.Infrastructure\\DevRepositories.Reports.cs".replace("src\\", ""), [
    ("                return Task.FromResult(entry.Value);",
     "                return Task.FromResult<Report?>(entry.Value);", 1),
])
patch("WorkTime.Infrastructure\\DevRepositories.Users.cs", [
    ("                return Task.FromResult(entry.Value);",
     "                return Task.FromResult<User?>(entry.Value);", 1),
])
patch("WorkTime.Infrastructure\\DevRepositories.Support.cs", [
    ("                return Task.FromResult(closure);",
     "                return Task.FromResult<PeriodClosure?>(closure);", 1),
])

print("DONE")

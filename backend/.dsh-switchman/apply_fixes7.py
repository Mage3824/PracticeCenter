# -*- coding: utf-8 -*-
# Оставшиеся nullable-warning'ы (аннотации и null-forgiving; поведение и тесты не меняются).
import sys

ROOT = r"E:\AItest\progcentr\project\backend"

def patch(rel, pairs):
    path = ROOT + "\\" + rel
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

patch("src\\WorkTime.Api\\Controllers\\AuthController.cs", [
    ("        var user = await _users.GetByLoginAsync(payload.login, cancellationToken);",
     "        var user = await _users.GetByLoginAsync(payload.login!, cancellationToken);", 1),
])
patch("tests\\WorkTime.Tests\\AuthServiceTests.cs", [
    ("        AuthResult fifthAttempt = null;",
     "        AuthResult fifthAttempt = null!;", 2),
])
patch("tests\\WorkTime.Tests\\InMemoryAbsenceRepository.cs", [
    ("    public InMemoryAbsenceRepository() : this(null)",
     "    public InMemoryAbsenceRepository() : this(null!)", 1),
])
patch("tests\\WorkTime.Tests\\InMemoryBreakRepository.cs", [
    ("    public InMemoryBreakRepository() : this(null)",
     "    public InMemoryBreakRepository() : this(null!)", 1),
])
patch("tests\\WorkTime.Tests\\InMemoryCommentRepository.cs", [
    ("    public InMemoryCommentRepository() : this(null)",
     "    public InMemoryCommentRepository() : this(null!)", 1),
])
patch("tests\\WorkTime.Tests\\InMemoryWorkDayRepository.cs", [
    ("    public InMemoryWorkDayRepository() : this(null)",
     "    public InMemoryWorkDayRepository() : this(null!)", 1),
])
patch("tests\\WorkTime.Tests\\InMemoryPeriodClosureRepository.cs", [
    ("                return Task.FromResult(closure);",
     "                return Task.FromResult<PeriodClosure?>(closure);", 1),
])
patch("tests\\WorkTime.Tests\\InMemoryReportRepository.cs", [
    ("                return Task.FromResult(entry.Value);",
     "                return Task.FromResult<Report?>(entry.Value);", 1),
])
patch("tests\\WorkTime.Tests\\InMemoryUserRepository.cs", [
    ("                return Task.FromResult(entry.Value);",
     "                return Task.FromResult<User?>(entry.Value);", 1),
])
patch("tests\\WorkTime.Tests\\IntegrationApiEndpointsTests.cs", [
    ("    public static string Text(JsonElement element, string name) => element.GetProperty(name).GetString();",
     "    public static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;", 1),
])
print("DONE")

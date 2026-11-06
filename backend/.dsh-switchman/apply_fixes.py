# -*- coding: utf-8 -*-
# Применение правок аудита (этап 2) к исходникам backend. Каждая пара old->new должна
# встречаться в файле ровно один раз — иначе скрипт останавливается и ничего не пишет.
import sys

ROOT = r"E:\AItest\progcentr\project\backend"

def patch(rel, pairs):
    path = ROOT + "\\src\\" + rel if rel.startswith(("WorkTime.",)) else ROOT + "\\" + rel
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        text = f.read()
    nl = "\r\n" if "\r\n" in text else "\n"
    work = text.replace("\r\n", "\n")
    for old, new in pairs:
        n = work.count(old)
        if n == 0 and new in work:
            print("SKIP %s (already applied)" % rel)
            continue
        if n != 1:
            print("FAIL %s: count=%d for %r" % (rel, n, old[:48]))
            sys.exit(1)
        work = work.replace(old, new)
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(work.replace("\n", nl))
    print("OK %s (%d)" % (rel, len(pairs)))

# Wire.cs удаляется отдельно по номерам строк (якорная проверка) — точный old не совпал.
_real_patch = patch
def patch(rel, pairs):
    if rel.endswith("Wire.cs"):
        print("DEFER %s" % rel)
        return
    _real_patch(rel, pairs)

# ---- Middleware.cs: единый envelope + активность BR32 на любом исходе запроса ----
patch("WorkTime.Api\\Middleware.cs", [
    ("""        var envelope = new Dictionary<string, object>
        {
            ["code"] = code,
            ["message"] = detail,
            ["httpStatus"] = status,
        };

        problem.Extensions["error"] = envelope;

        return problem;
    }
}""",
     """        problem.Extensions["error"] = ErrorEnvelope.TryEnvelope(code, detail, status);

        return problem;
    }
}

// Единственное место сборки envelope error{code,message,httpStatus} из api-contracts.md §1:
// exception handler, BR32 middleware и MVC-валидация используют только его.
public static class ErrorEnvelope
{
    public static Dictionary<string, object> TryEnvelope(string code, string message, int httpStatus)
    {
        return new Dictionary<string, object>
        {
            ["code"] = code,
            ["message"] = message,
            ["httpStatus"] = httpStatus,
        };
    }
}"""),
    ("""            problem.Extensions["error"] = new Dictionary<string, object>
            {
                ["code"] = "SESSION_EXPIRED",
                ["message"] = problem.Detail!,
                ["httpStatus"] = StatusCodes.Status401Unauthorized,
            };""",
     """            problem.Extensions["error"] = ErrorEnvelope.TryEnvelope(
                "SESSION_EXPIRED", problem.Detail!, StatusCodes.Status401Unauthorized);"""),
    ("""        await next(context);

        // Only a request the server accepted counts as activity.
        if (context.Response.StatusCode < StatusCodes.Status400BadRequest)
        {
            _tracker.TryRecordActivity(subject, now);
        }""",
     """        // BR32 (правка аудита): активность продлевается любым аутентифицированным
        // запросом — даже 400/409/404 через исключение показывает, что пользователь работает.
        try
        {
            await next(context);
        }
        finally
        {
            _tracker.TryRecordActivity(subject, DateTime.UtcNow);
        }"""),
])

# ---- Wire.cs: мёртвый ErrorEnvelopeWire удалён (envelope строится в ErrorEnvelope) ----
patch("WorkTime.Api\\Wire.cs", [
    (r"""// The error envelope of api-contracts.md: {"error":{"code","message",...}}. Written as text because
// a business-rule rejection must not carry entityType/entityId keys at all, while a missing entity
// must carry them - a nullable-field DTO would emit both shapes with nulls.
public class ErrorEnvelopeWire
{
    // Body of a 409 (business rule) or a 400 (request validation).
    public static string TryBusinessRuleJson(string code, string message, int httpStatus)
    {
        return "{\"error\":{\"code\":\"" + Escape(code) + "\",\"message\":\"" + Escape(message)
            + "\",\"httpStatus":" + httpStatus.ToString(CultureInfo.InvariantCulture) + "}}";
    }

    // Body of a 404: the missing entity is named so the client can react to it.
    public static string TryEntityNotFoundJson(string entityType, Guid entityId, string message, int httpStatus)
    {
        return "{\"error\":{\"code\":\"ENTITY_NOT_FOUND\",\"entityType\":\"" + Escape(entityType)
            + "\",\"entityId\":\"" + entityId.ToString("D", CultureInfo.InvariantCulture)
            + "\",\"message\":\"" + Escape(message)
            + "\",\"httpStatus":" + httpStatus.ToString(CultureInfo.InvariantCulture) + "}}";
    }

    private static string Escape(string text)
    {
        var result = new StringBuilder();

        foreach (var character in text)
        {
            if (character == '"' || character == '\\')
            {
                result.Append('\\');
                result.Append(character);
                continue;
            }

            if (character < ' ')
            {
                // Control characters would break the JSON line; the frontend never reads them.
                result.Append(' ');
                continue;
            }

            result.Append(character);
        }

        return result.ToString();
    }
}""",
     """// envelope error{code,message,httpStatus} из api-contracts.md §1 строится в одном месте —
// ErrorEnvelope.TryEnvelope (Middleware.cs); текстовые builder'ы здесь были мёртвым кодом."""),
])

# ---- Program.cs: форма валидационной ошибки + usings ----
patch("WorkTime.Api\\Program.cs", [
    ("using Microsoft.OpenApi.Models;",
     """using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;"""),
    ("    builder.Services.AddControllers();",
     """    builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
    {
        // api-contracts.md §1: отклонение model-state ([Required]/[RegularExpression]) тоже
        // отдаёт envelope code/message/httpStatus + error{...}, а не ProblemDetails по умолчанию.
        options.InvalidModelStateResponseFactory = context =>
        {
            var reasons = new List<string>();
            foreach (var entry in context.ModelState)
            {
                var entryErrors = entry.Value?.Errors;
                if (entryErrors == null)
                {
                    continue;
                }

                foreach (var error in entryErrors.Errors)
                {
                    reasons.Add(entry.Key + ": " + error.ErrorMessage);
                }
            }

            var detail = reasons.Count == 0
                ? "[RU] Request validation failed."
                : "[RU] Request validation failed. " + string.Join("; ", reasons);

            var problem = new ProblemDetails
            {
                Type = "https://errors.worktime/validation",
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest,
                Detail = detail,
            };
            problem.Extensions["code"] = "VALIDATION";
            problem.Extensions["message"] = detail;
            problem.Extensions["httpStatus"] = StatusCodes.Status400BadRequest;
            problem.Extensions["error"] = ErrorEnvelope.TryEnvelope(
                "VALIDATION", detail, StatusCodes.Status400BadRequest);

            return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
        };
    });"""),
])

# ---- AuthController.cs: активность при логине + логин в claim ----
patch("WorkTime.Api\\Controllers\\AuthController.cs", [
    ("""    private readonly RequestContextProvider _requestContext;

    public AuthController(
        AuthService authService,
        IUserRepository users,
        TokenService tokenService,
        RequestContextProvider requestContext)
    {
        _authService = authService;
        _users = users;
        _tokenService = tokenService;
        _requestContext = requestContext;
    }""",
     """    private readonly RequestContextProvider _requestContext;

    private readonly UserActivityTracker _activity;

    public AuthController(
        AuthService authService,
        IUserRepository users,
        TokenService tokenService,
        RequestContextProvider requestContext,
        UserActivityTracker activity)
    {
        _authService = authService;
        _users = users;
        _tokenService = tokenService;
        _requestContext = requestContext;
        _activity = activity;
    }"""),
    ("""        var accessToken = await _tokenService.TryIssueAccessToken(user.Id, user.Role, cancellationToken);""",
     """        // BR32 (правка аудита): успешный вход восстанавливает baseline активности,
        // иначе просроченная запись блокировала бы сессию навсегда.
        _activity.TryRecordActivity(user.Id.ToString(), DateTime.UtcNow);

        var accessToken = await _tokenService.TryIssueAccessToken(user.Id, user.Role, user.Login, cancellationToken);"""),
])

# ---- TokenService.cs: ClaimTypes.Name = логин ----
patch("WorkTime.Api\\TokenService.cs", [
    ("    public Task<string> TryIssueAccessToken(Guid userId, UserRole role, CancellationToken cancellationToken)",
     "    public Task<string> TryIssueAccessToken(Guid userId, UserRole role, string login, CancellationToken cancellationToken)"),
    ("            new Claim(ClaimTypes.Name, userId.ToString()),",
     "            new Claim(ClaimTypes.Name, login),"),
])

# ---- JwtSettings.cs: без тихой подмены значения ----
patch("WorkTime.Api\\JwtSettings.cs", [
    ("""        var settings =
            configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();

        if (string.IsNullOrWhiteSpace(settings.SecretKey))""",
     """        var settings = configuration.GetSection("JwtSettings").Get<JwtSettings>();
        if (settings == null)
        {
            // Отсутствие секции вне Development — ошибка конфигурации; в Development —
            // явный dev-дефолт (тихая подмена вне Development недопустима).
            if (!environment.IsDevelopment())
            {
                throw new InvalidRequestException(
                    "CONFIGURATION",
                    "[RU] The JwtSettings section must be configured outside Development.");
            }

            settings = new JwtSettings
            {
                SecretKey = JwtSettings.DefaultDevelopmentSecretKey,
                Issuer = "WorkTime.Api",
                Audience = "WorkTime.Client",
            };
        }

        if (string.IsNullOrWhiteSpace(settings.SecretKey))"""),
    ("""        if (settings.ExpiryMinutes <= 0)
        {
            settings.ExpiryMinutes = JwtSettings.DefaultExpiryMinutes;
        }

        ValidateSettings(settings);""",
     """        // Молча не чиним ExpiryMinutes: невалидное значение отдаёт ValidateSettings как
        // явную ошибку конфигурации (правка аудита).

        ValidateSettings(settings);"""),
])

# ---- WorkDaysController.cs / BreaksController.cs: все аутентифицированные роли ----
patch("WorkTime.Api\\Controllers\\WorkDaysController.cs", [
    ('[Authorize(Policy = "EmployeeOnly")]',
     """// Все аутентифицированные роли (BR19, BR3): владение днём проверяет сервис (BR21 -> 409).
[Authorize]"""),
])
patch("WorkTime.Api\\Controllers\\BreaksController.cs", [
    ('[Authorize(Policy = "EmployeeOnly")]',
     """// Все аутентифицированные роли (BR19): владение днём проверяет сервис (BR21 -> 409).
[Authorize]"""),
])

# ---- AbsencesController.cs: BR27 — сотрудник читает собственный статус ----
patch("WorkTime.Api\\Controllers\\AbsencesController.cs", [
    ('[Authorize(Policy = "ManagerOrAdmin")]',
     """// Все аутентифицированные роли: управленческие пути закрыты политикой на методах,
// сотрудник читает только собственный статус (BR27).
[Authorize]"""),
    ('    [HttpPost("{employeeId}")]',
     """    [HttpPost("{employeeId}")]
    [Authorize(Policy = "ManagerOrAdmin")]"""),
    ('    [HttpPut("{employeeId}")]',
     """    [HttpPut("{employeeId}")]
    [Authorize(Policy = "ManagerOrAdmin")]"""),
    ('    [HttpGet("")]',
     """    [HttpGet("")]
    [Authorize(Policy = "ManagerOrAdmin")]"""),
    ("""        var day = RequestParsers.TryParseDateText(date, "date");

        var found = await _absences.GetForEmployeeOnDateAsync(employeeId, day, cancellationToken);""",
     """        var day = RequestParsers.TryParseDateText(date, "date");

        // BR27: сотрудник читает статус по своему employeeId; чужой id -> 403.
        var subjectId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        if (!User.IsInRole("Manager") && !User.IsInRole("Admin") && employeeId != subjectId)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var found = await _absences.GetForEmployeeOnDateAsync(employeeId, day, cancellationToken);"""),
])

# ---- AdminController.cs: маршрут по контракту + массив в Swagger ----
patch("WorkTime.Api\\Controllers\\AdminController.cs", [
    ("(the path this Stage gives it: GET /api/admin/audit)",
     "(the path this Stage gives it: GET /api/audit-logs)"),
    ('    [HttpGet("audit")]',
     '    [HttpGet("/api/audit-logs")]'),
    ("    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuditLogWire))]",
     "    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuditLogWire[]))]"),
])

# ---- ReportsController.cs: JSON null вместо пустого тела (BR15) ----
patch("WorkTime.Api\\Controllers\\ReportsController.cs", [
    ("    public async Task<DailyReportWire> TryGetDailyReport(Guid workDayId)",
     "    public async Task<IActionResult> TryGetDailyReport(Guid workDayId)"),
    ("""        if (report == null)
        {
            return null;
        }""",
     """        if (report == null)
        {
            // BR15: незавершённый день -> JSON null (контракт §1), а не пустое тело.
            return new ContentResult
            {
                ContentType = "application/json",
                StatusCode = StatusCodes.Status200OK,
                Content = "null",
            };
        }"""),
    ("        return WireConversion.TryFromDailyReport(report);",
     "        return Ok(WireConversion.TryFromDailyReport(report));"),
])

# ---- BR34: хеш пароля при записи ----
patch("WorkTime.Api\\Controllers\\UsersController.cs", [
    ("""            // BR34: хешер появляется в Infrastructure на этапе 4; до него это тот же placeholder,
            // который сравнивает AuthService — иначе созданный пользователь не смог бы войти.
            PasswordHash = password,""",
     """            // BR34: пароль пишется хешем PasswordHasher — открытый пароль не храним.
            PasswordHash = PasswordHasher.TryHash(password),"""),
])
patch("WorkTime.Api\\DevSeed.cs", [
    ("            PasswordHash = password,",
     "            PasswordHash = PasswordHasher.TryHash(password),"),
])

# ---- AuthService.cs: сверка хеша + lock на мутации User ----
patch("WorkTime.Application\\Services\\AuthService.cs", [
    ("""        if (TryMatchesPassword(user, password))
        {
            user.FailedLoginAttempts = 0;
            user.Locked = false;
            user.LockedUntil = null;
            await _users.SaveAsync(user, cancellationToken);""",
     """        if (TryMatchesPassword(user, password))
        {
            // Потокобезопасность: User — общий объект dev-репозитория, правим под lock.
            lock (EntityMutationGates.Users)
            {
                user.FailedLoginAttempts = 0;
                user.Locked = false;
                user.LockedUntil = null;
                await _users.SaveAsync(user, cancellationToken);
            }"""),
    ("""        user.FailedLoginAttempts = user.FailedLoginAttempts + 1;

        var lockRulesApply = user.Role == UserRole.Admin || user.Role == UserRole.Manager;

        var requiresCaptcha = false;
        var isLocked = false;
        DateTime? lockedUntil = null;
        string message;

        if (lockRulesApply && user.FailedLoginAttempts >= WorkTimeRules.MaxFailedAttempts)
        {
            // BR35: blocked until the end of the day; only an administrator unlocks
            // it early (user.Locked stays true).
            user.Locked = true;
            lockedUntil = EndOfCalendarDayFor(now);
            user.LockedUntil = lockedUntil;
            isLocked = true;
            message = "[RU] Account blocked until the end of the day. Only an administrator can unlock it.";
        }
        else if (lockRulesApply && user.FailedLoginAttempts >= WrongPasswordsToLock)
        {
            // BR35: locked for 15 minutes.
            lockedUntil = now + LockDuration;
            user.LockedUntil = lockedUntil;
            isLocked = true;
            message = "[RU] Account is locked for 15 minutes.";
        }
        else if (lockRulesApply && user.FailedLoginAttempts >= WorkTimeRules.CaptchaThreshold)
        {
            // BR36 + the BR35 countdown notification from the 2nd wrong password on.
            requiresCaptcha = true;
            var remaining = WrongPasswordsToLock - user.FailedLoginAttempts;
            message = "[RU] Wrong password. CAPTCHA required. " + remaining + " attempt(s) before the lock.";
        }
        else
        {
            message = "[RU] Wrong password.";
        }

        await _users.SaveAsync(user, cancellationToken);""",
     """        var requiresCaptcha = false;
        var isLocked = false;
        DateTime? lockedUntil = null;
        string message;

        // Потокобезопасность: счётчик и блокировка правятся на общем объекте User
        // (dev-репозиторий singleton) — read-modify-write под одним lock.
        lock (EntityMutationGates.Users)
        {
            user.FailedLoginAttempts = user.FailedLoginAttempts + 1;

            var lockRulesApply = user.Role == UserRole.Admin || user.Role == UserRole.Manager;

            if (lockRulesApply && user.FailedLoginAttempts >= WorkTimeRules.MaxFailedAttempts)
            {
                // BR35: blocked until the end of the day; only an administrator unlocks
                // it early (user.Locked stays true).
                user.Locked = true;
                lockedUntil = EndOfCalendarDayFor(now);
                user.LockedUntil = lockedUntil;
                isLocked = true;
                message = "[RU] Account blocked until the end of the day. Only an administrator can unlock it.";
            }
            else if (lockRulesApply && user.FailedLoginAttempts >= WrongPasswordsToLock)
            {
                // BR35: locked for 15 minutes.
                lockedUntil = now + LockDuration;
                user.LockedUntil = lockedUntil;
                isLocked = true;
                message = "[RU] Account is locked for 15 minutes.";
            }
            else if (lockRulesApply && user.FailedLoginAttempts >= WorkTimeRules.CaptchaThreshold)
            {
                // BR36 + the BR35 countdown notification from the 2nd wrong password on.
                requiresCaptcha = true;
                var remaining = WrongPasswordsToLock - user.FailedLoginAttempts;
                message = "[RU] Wrong password. CAPTCHA required. " + remaining + " attempt(s) before the lock.";
            }
            else
            {
                message = "[RU] Wrong password.";
            }

            await _users.SaveAsync(user, cancellationToken);
        }"""),
    ("""    // BR34: passwords are stored hashed, so the comparison must be done against the
    // hash. The hashing itself lives in Infrastructure/credentials handling; here it
    // is a single point to wire the real hasher in.
    private static bool TryMatchesPassword(User user, string password)
    {
        var candidateHash = HashPasswordPlaceholder(password);
        return user.PasswordHash == candidateHash;
    }

    private static string HashPasswordPlaceholder(string password)
    {
        // TODO: replace with the real one-way hash used by the user repository (BR34).
        return password;
    }""",
     """    // BR34: passwords are stored hashed, so the comparison runs against the stored
    // PBKDF2 hash (PasswordHasher), compared in constant time.
    private static bool TryMatchesPassword(User user, string password)
    {
        return PasswordHasher.TryVerify(password, user.PasswordHash);
    }"""),
])

# ---- WorkTimeService.cs: lock на мутации дня и перерывов ----
patch("WorkTime.Application\\Services\\WorkTimeService.cs", [
    ("""        workDay.EndTime = endTime;
        workDay.Status = WorkDayStatus.Finished;

        var workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);

        // BR5: less than 15 minutes worked means the day is not counted in reports.
        workDay.CountedInReports = workedTime >= WorkTimeRules.MinDayDuration;

        await _workDays.SaveAsync(workDay, cancellationToken);
        return workDay;""",
     """        // Потокобезопасность: WorkDay и его перерывы — общие объекты; правим под lock.
        lock (EntityMutationGates.WorkTime)
        {
            workDay.EndTime = endTime;
            workDay.Status = WorkDayStatus.Finished;

            var workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);

            // BR5: less than 15 minutes worked means the day is not counted in reports.
            workDay.CountedInReports = workedTime >= WorkTimeRules.MinDayDuration;

            await _workDays.SaveAsync(workDay, cancellationToken);
        }
        return workDay;"""),
    ("""        var previousEndTime = workDay.EndTime == null ? string.Empty : workDay.EndTime.ToString();
        workDay.EndTime = EndOfCalendarDay;
        workDay.Status = WorkDayStatus.AutoFinished;

        var workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);
        workDay.CountedInReports = workedTime >= WorkTimeRules.MinDayDuration;

        await _workDays.SaveAsync(workDay, cancellationToken);""",
     """        // Потокобезопасность: те же общие объекты, что и в FinishWorkDayAsync.
        string previousEndTime;
        TimeSpan workedTime;
        lock (EntityMutationGates.WorkTime)
        {
            previousEndTime = workDay.EndTime == null ? string.Empty : workDay.EndTime.ToString();
            workDay.EndTime = EndOfCalendarDay;
            workDay.Status = WorkDayStatus.AutoFinished;

            workedTime = await CalculateWorkedTimeAsync(workDay, cancellationToken);
            workDay.CountedInReports = workedTime >= WorkTimeRules.MinDayDuration;

            await _workDays.SaveAsync(workDay, cancellationToken);
        }"""),
    ("""        foreach (var workBreak in dayBreaks)
        {
            if (workBreak.EndTime != null)
            {
                continue;
            }

            var duration = endTime - workBreak.StartTime;
            workBreak.EndTime = endTime;
            workBreak.Duration = duration;

            // BR8: a break shorter than 5 minutes is not counted anywhere.
            workBreak.CountedInReports = duration >= WorkTimeRules.MinBreakDuration;

            await _breaks.SaveAsync(workBreak, cancellationToken);
        }""",
     """        // Потокобезопасность: перерывы дня — общие объекты; вызывается внутри lock WorkTime,
        // lock реентрален.
        lock (EntityMutationGates.WorkTime)
        {
            foreach (var workBreak in dayBreaks)
            {
                if (workBreak.EndTime != null)
                {
                    continue;
                }

                var duration = endTime - workBreak.StartTime;
                workBreak.EndTime = endTime;
                workBreak.Duration = duration;

                // BR8: a break shorter than 5 minutes is not counted anywhere.
                workBreak.CountedInReports = duration >= WorkTimeRules.MinBreakDuration;

                await _breaks.SaveAsync(workBreak, cancellationToken);
            }
        }"""),
])

# ---- BreakService.cs: lock на мутации перерыва и дня ----
patch("WorkTime.Application\\Services\\BreakService.cs", [
    ("""        // BR9: the limit is recorded, never enforced by rejection. The break finishes
        // and the day carries the flag that turns into the manager notification.
        var otherBreaks = await SumCountedBreakTimeAsync(workBreak.WorkDayId, workBreak.Id, cancellationToken);
        if (otherBreaks + duration > WorkTimeRules.MaxBreakDuration)
        {
            workDay.IsBreakLimitExceeded = true;
            await _workDays.SaveAsync(workDay, cancellationToken);
        }

        workBreak.EndTime = endTime;
        workBreak.Duration = duration;

        // BR8: shorter than 5 minutes -> not counted in worked time and not saved in
        // reports. This is NOT an error, the break is simply excluded.
        workBreak.CountedInReports = duration >= WorkTimeRules.MinBreakDuration;

        await _breaks.SaveAsync(workBreak, cancellationToken);
        return workBreak;""",
     """        // Потокобезопасность: перерыв и день — общие объекты dev-репозиториев;
        // read-modify-write под одним lock.
        lock (EntityMutationGates.WorkTime)
        {
            // BR9: the limit is recorded, never enforced by rejection. The break finishes
            // and the day carries the flag that turns into the manager notification.
            var otherBreaks = await SumCountedBreakTimeAsync(workBreak.WorkDayId, workBreak.Id, cancellationToken);
            if (otherBreaks + duration > WorkTimeRules.MaxBreakDuration)
            {
                workDay.IsBreakLimitExceeded = true;
                await _workDays.SaveAsync(workDay, cancellationToken);
            }

            workBreak.EndTime = endTime;
            workBreak.Duration = duration;

            // BR8: shorter than 5 minutes -> not counted in worked time and not saved in
            // reports. This is NOT an error, the break is simply excluded.
            workBreak.CountedInReports = duration >= WorkTimeRules.MinBreakDuration;

            await _breaks.SaveAsync(workBreak, cancellationToken);
        }
        return workBreak;"""),
])

# ---- AbsenceService.cs: поиск+запись статуса под lock (BR28) ----
patch("WorkTime.Application\\Services\\AbsenceService.cs", [
    ("""        // BR28: changing an existing status reuses its Id so that history stays
        // attached to one absence record for the day.
        var existing = await TryFindAbsenceForAsync(employeeId, date, cancellationToken);
        if (existing != null)
        {
            existing.Reason = reason;

            // BR25/BR30: the actor who set the current status is recorded, so the
            // BR27 tooltip can name whom the employee has to contact.
            existing.CreatedByUserId = actorId;

            await _absences.SaveAsync(existing, cancellationToken);
            return existing;
        }

        var absence = new Absence
        {
            Id = Guid.NewGuid(),
            UserId = employeeId,
            Date = date.Date,
            Reason = reason,

            // BR25: the administrator or manager who set this status (BR30 author).
            CreatedByUserId = actorId,
        };

        await _absences.SaveAsync(absence, cancellationToken);
        return absence;""",
     """        // Потокобезопасность: поиск и запись под одним lock (BR28 переиспользует id).
        lock (EntityMutationGates.Absences)
        {
            // BR28: changing an existing status reuses its Id so that history stays
            // attached to one absence record for the day.
            var existing = await TryFindAbsenceForAsync(employeeId, date, cancellationToken);
            if (existing != null)
            {
                existing.Reason = reason;

                // BR25/BR30: the actor who set the current status is recorded, so the
                // BR27 tooltip can name whom the employee has to contact.
                existing.CreatedByUserId = actorId;

                await _absences.SaveAsync(existing, cancellationToken);
                return existing;
            }

            var absence = new Absence
            {
                Id = Guid.NewGuid(),
                UserId = employeeId,
                Date = date.Date,
                Reason = reason,

                // BR25: the administrator or manager who set this status (BR30 author).
                CreatedByUserId = actorId,
            };

            await _absences.SaveAsync(absence, cancellationToken);
            return absence;
        }"""),
])

print("ALL PATCHES APPLIED")

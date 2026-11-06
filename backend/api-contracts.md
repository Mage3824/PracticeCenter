# WorkTime — API Contracts (Stage 1)

Handover document for the frontend intern. Everything below is derived from code that
currently compiles and from the **29 passing tests** (`tests/WorkTime.Tests`), not from
an aspiration. Each endpoint row names the exact service method that answers it.

Status markers used throughout:

| Marker | Meaning |
| --- | --- |
| `IMPL` | Method exists in `src/WorkTime.Application/Services`, behaviour is covered by green tests. |
| `RESERVED · Stage 2` | Wire shape decided now, implementation arrives with the Web API layer (controllers, JWT, session). |
| `RESERVED · Stage 4` | Needs the Infrastructure layer (real DB, backup, hashers) — do not build against it yet. |

No Swagger/OpenAPI file is produced at this stage on purpose: the API surface is about to
change in Stage 2, so a machine-readable schema would go stale immediately. This markdown
is the contract; the JSON samples are copy-pasteable.

---

## 1. Conventions

- **Transport.** HTTPS only. `Content-Type: application/json; charset=utf-8` for request and
  response bodies. Dates in bodies are **server calendar dates**, not browser-local dates
  (the server owns the working-day boundary — BR3 auto-finish at 23:59:59 is a server fact).
- **Authentication.** `Authorization: Bearer <jwt>` (`RESERVED · Stage 2`). Until then, the
  ids that the service methods take explicitly (`employeeId`, `currentUserId`, `editorId`,
  `actorId`) are sent in the body; from Stage 2 on they come from the token subject and
  **stop appearing in request bodies** (see §6).
- **Success status.** `200 OK` with the DTO named by the endpoint row. A service method that
  can answer "nothing" (`null`, empty list) returns `200` with `null` / `[]`, never `404`.
- **Business-rule violation.** `409 Conflict` + error envelope carrying the BR code. The code
  is a stable machine-readable token (`"BR1"`), so the frontend can pick its tooltip text and
  never parse the human message.
- **Missing entity.** `404 Not Found` + error envelope with `entityType` / `entityId`.
- **Message strings.** Every server message currently starts with `[RU]` — a localization
  placeholder. Treat them as display-only; they are not part of any decision logic.

Error envelope (business rule):

```json
{
  "error": {
    "code": "BR1",
    "message": "[RU] Work day already open for this employee - finish it before starting a new one (BR1).",
    "httpStatus": 409
  }
}
```

Error envelope (missing entity, e.g. `EntityNotFoundException(workDayId, "WorkDay")`):

```json
{
  "error": {
    "code": "ENTITY_NOT_FOUND",
    "entityType": "WorkDay",
    "entityId": "5a1c9b0e-2f31-4a6d-9b7c-0d2e4f60a8b1",
    "message": "[RU] No such working day.",
    "httpStatus": 404
  }
}
```

---

## 2. Endpoint table

| # | HTTP (hypothetical) | Service method (`WorkTime.Application/Services`) | Request body / params | Success response | Business rules enforced |
| --- | --- | --- | --- | --- | --- |
| 1 | `POST /api/auth/login` | `AuthService.LoginAsync(login, password, ct)` `IMPL` | `{ login, password }` | `AuthResult` | BR35, BR36 — **never thrown**: lock, countdown and CAPTCHA come back as flags |
| 2 | `POST /api/work-days/start` | `WorkTimeService.StartWorkDayAsync(employeeId, date, startTime, ct)` `IMPL` | `{ employeeId*, date, startTime }` | `WorkDayDto` | BR1 (`409`), BR27 (`409`) |
| 3 | `POST /api/work-days/{workDayId}/finish` | `WorkTimeService.FinishWorkDayAsync(employeeId, workDayId, endTime, ct)` `IMPL` | `{ employeeId*, endTime }` | `WorkDayDto` | BR2 (`409`), BR21 ownership (`409`), missing day (`404`) — side effect: BR7 auto-closes an open break |
| 4 | `POST /api/work-days/{workDayId}/auto-finish` | `WorkTimeService.AutoFinishWorkDayAsync(employeeId, workDayId, ct)` `IMPL` (server-triggered by the clock; the endpoint exists for the admin's emergency view) | `{ employeeId* }` | `WorkDayDto` | BR2 (`409` "not open"), missing day (`404`) — writes the `"Auto-finish"` event-log entry (BR3) |
| 5 | `POST /api/work-days/{workDayId}/breaks/start` | `BreakService.StartBreakAsync(employeeId, workDayId, startTime, ct)` `IMPL` | `{ employeeId*, startTime }` | `BreakDto` | BR6 (`409`), BR10 (`409`), BR21 ownership (`409`), missing day (`404`) — BR9 is **not** an error, it sets `isBreakLimitExceeded` |
| 6 | `POST /api/breaks/{breakId}/finish` | `BreakService.FinishBreakAsync(breakId, endTime, ct)` `IMPL` | `{ endTime }` | `BreakDto` | BR10 already-finished (`409`), BR6 day not open (`409`), BR8 end earlier than start (`409`), missing break/day (`404`) — BR8 for a break **shorter** than 5 min is silent: `countedInReports = false` |
| 7 | `POST /api/reports/{workDayId}` | `ReportService.CreateOrUpdateReportAsync(editorId, workDayId, text, ct)` `IMPL` | `{ editorId*, text }` | `ReportDto` | BR21/BR22 ownership (`409`), BR14 day not finished yet (`409`), BR12 editing window closed (`409` — only admin may edit after the day ended), missing `WorkDay`/`User` (`404`) |
| 8 | `GET /api/reports/daily/{workDayId}` | `ReportService.GetDailyReportAsync(currentUserId, workDayId, ct)` `IMPL` | path param + `{ currentUserId* }` | `DailyReport` or `null` (BR15: unfinished day ⇒ `null`) | BR21 (`409`), missing `WorkDay`/`User` (`404`) |
| 9 | `GET /api/reports/monthly` | `ReportService.GetMonthlyReportAsync(periodClosureId, departmentId, ct)` `IMPL` | query: `periodClosureId?`, `departmentId?` (see §6) | `List<MonthlyReport>` (`[]` when no window can be resolved) | none thrown — BR15, BR16, BR26, BR5, BR4/BR11 shape the payload |
| 10 | `GET /api/work-days/{employeeId}/can-start` | `AbsenceService.CanStartWorkDayAsync(employeeId, date, ct)` `IMPL` (this is exactly the state of the "Start work" button) | query: `date` | `{ "canStart": true }` | BR27 reported as `false`; the same fact throws BR27 on endpoint 2 |
| 11 | `POST /api/absences/{employeeId}` | `AbsenceService.SetAbsenceAsync(actorId, employeeId, date, reason, ct)` `IMPL` | `{ actorId*, date, reason }` | `AbsenceDto` | BR25/BR28 role gate (`409`), missing actor/target `User` (`404`) — BR28: changing an existing status reuses its id |
| 12 | `POST /api/reports/{workDayId}/comments` | *no service method exists* | `{ reportId, text }` | `CommentDto` | **RESERVED · Stage 2** — BR13 (manager comments; visible to manager/admin only) |
| 13 | `GET /api/reports/monthly/export.xlsx` | *no service method exists* | query as endpoint 9 | binary | **RESERVED · Stage 2** — BR17 export gate (Manager, Administrator only), BR18 custom-period export |
| 14 | `POST /api/period-closures/{id}/close`, `POST /api/period-closures/{id}/reopen` | *no service method exists* | see `PeriodClosureDto` | `PeriodClosureDto` | **RESERVED · Stage 2** — BR23.1 closing of the billing period, BR23.2 reopen with mandatory reason ("Emergency correction") |
| 15 | `GET /api/audit-logs` | read path exists in the repository layer only (`IAuditLogRepository.GetHistoryForEntityAsync`) | query: `entityId` | `List<AuditLogDto>` | **RESERVED · Stage 2** — BR29 admin-only access, BR30 previous value + author + time; today only the BR3 auto-finish writes an entry |

`*` = id that is redundant from Stage 2 on (comes from the JWT subject). The current
signatures keep it because there is no HTTP layer yet.

---

## 3. Scalar type mapping (the serialization decisions)

| C# type (domain/DTO) | JSON | Rule of thumb / why |
| --- | --- | --- |
| `Guid` | `string`, canonical UUID text (`"9b2f…"`), never `null` | ids are opaque; the frontend stores them and sends them back untouched |
| `Guid?` | `string` or `null` | e.g. `Absence.CreatedByUserId`, `Department.ParentId` |
| `TimeSpan` **time-of-day** (`StartTime`, `EndTime`) | `string` `"HH:mm"`, 24 h, no seconds | the UI shows minutes; seconds never matter for a clock time. Round-trip is exact for the values the system produces |
| `TimeSpan?` time-of-day | `"HH:mm"` or `null` | `null` = shift/break still open (BR2/BR15, BR10) |
| `TimeSpan` **duration** (`Duration`, `TotalHours`) | `number` = **integer minutes** (`480` = 8 h) | arithmetic on the frontend stays trivial; no `"7:00"` parsing. Wire keys of durations are renamed with a `Minutes` suffix so a key never promises "hours" while carrying minutes (see §5 tables) |
| `TimeSpan?` duration | integer minutes or `null` | `null` = break still open |
| `DateTime` **date-only** (`WorkDay.Date`, `Absence.Date`, `MonthlyReport.AbsenceDays`, `HoursByDay` keys) | `string` `"YYYY-MM-DD"` | the domain stores these with a zero time part; serializing midnight noise would create timezone bugs |
| `DateTime` **timestamp** (`CreatedAt`, `UpdatedAt`, `ClosedAt`, `AuditLog.Timestamp`, `LockedUntil`) | `string` `"YYYY-MM-DDTHH:mm:ss"`, server time, **no timezone suffix** | the server is the clock owner; BR32 (session expiry) and any timezone policy are **RESERVED · Stage 4** |
| `DateTime?` timestamp | as above or `null` | `AuthResult.LockedUntil = null` ⇒ no lock |
| `bool` | `true` / `false` | |
| `int` (`Year`, `Month`, `FailedLoginAttempts`) | JSON number | |
| enum (`UserRole`, `WorkDayStatus`, `AbsenceReason`, `AuditAction`) | `string` = C# enumerator name, exact case | stable tokens; never send the ordinal — renaming a member would silently renumber every client |
| `List<T>` | JSON array, `[]` when empty (never `null`) | |
| `Dictionary<DateTime, TimeSpan>` (`MonthlyReport.HoursByDay`) | JSON object keyed by `"YYYY-MM-DD"`, value = integer minutes | sparse days must stay sparse (BR15/BR26/BR5 skip days), so an array of 28/31 slots is the wrong shape |
| nullable reference (`DailyReport?` result) | `null` body for endpoint 8 | "no report exists", not "report is empty" |

---

## 4. Enum wire values

| Enum (C#) | Wire tokens |
| --- | --- |
| `UserRole` | `"Admin"`, `"Manager"`, `"Employee"` |
| `WorkDayStatus` | `"Active"`, `"Finished"`, `"AutoFinished"` |
| `AbsenceReason` | `"Sick"`, `"Leave"`, `"DayOff"`, `"Absence"` |
| `AuditAction` | `"Create"`, `"Update"`, `"Delete"` |

The frontend maps tokens → its own labels; it must fail loudly on an unknown token rather
than fall back to a default (a new enumerator is a contract change, not a bug to absorb).

---

## 5. DTO field tables

Wire key = camelCase of the C# member unless the table says otherwise. "Notes" column keeps
the BR rule that gives the field its meaning.

### WorkDayDto (`src/WorkTime.Application/Dto/WorkDayDto.cs`) — response of endpoints 2, 3, 4

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | addressed by it everywhere (`workDayId` path param) |
| `UserId` | `userId` | string uuid | owner; BR21 ownership gate reads exactly this |
| `Date` | `date` | `"YYYY-MM-DD"` | calendar day; BR1 is per calendar date (so a new day after midnight is legal — BR3) |
| `StartTime` | `startTime` | `"HH:mm"` | |
| `EndTime` | `endTime` | `"HH:mm"` \| null | null ⇒ open shift |
| `Status` | `status` | `"Active"`/`"Finished"`/`"AutoFinished"` | BR2 guard, BR3 sets `AutoFinished` |
| `CountedInReports` | `countedInReports` | boolean | BR5: false when < 15 min worked ⇒ day absent from monthly table |
| `IsBreakLimitExceeded` | `isBreakLimitExceeded` | boolean | BR9: > 4 h of breaks is **not** an error; this flag turns into the manager notification |

### BreakDto (`src/WorkTime.Application/Dto/BreakDto.cs`) — response of endpoints 5, 6

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | |
| `WorkDayId` | `workDayId` | string uuid | BR6: the break belongs to one working day |
| `StartTime` | `startTime` | `"HH:mm"` | |
| `EndTime` | `endTime` | `"HH:mm"` \| null | null ⇒ still open (BR10: at most one per employee) |
| `Duration` | `durationMinutes` | integer \| null | duration, minutes |
| `CountedInReports` | `countedInReports` | boolean | BR8: false for < 5 min — not subtracted, not in report totals |

### ReportDto (`src/WorkTime.Application/Dto/ReportDto.cs`) — response of endpoint 7 (the **text** report)

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | |
| `WorkDayId` | `workDayId` | string uuid | the finished day it describes |
| `Text` | `text` | string | |
| `CreatedAt` | `createdAt` | timestamp | |
| `UpdatedAt` | `updatedAt` | timestamp \| null | set only after an edit; BR12/BR30 |

### DailyReport (`src/WorkTime.Application/Dto/Reports.cs`) — response of endpoint 8

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `StartTime` | `startTime` | `"HH:mm"` | BR14: start/end of work |
| `EndTime` | `endTime` | `"HH:mm"` \| null | BR15 ⇒ a day without end time produces no report at all (`null` body) |
| `Breaks` | `breaks` | array of `ReportBreak` | BR14: **all** breaks of the day, list (not dictionary) |
| `TotalHours` | `totalMinutes` | integer minutes | BR4 + BR11 + BR8: span minus counted breaks; key renamed to avoid an "hours" lie |

`ReportBreak`: `startTime` `"HH:mm"`, `endTime` `"HH:mm"` \| null.

### MonthlyReport (`src/WorkTime.Application/Dto/Reports.cs`) — element of endpoint 9

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `EmployeeName` | `employeeName` | string | BR16 row label (full name, as stored) |
| `HoursByDay` | `hoursByDay` | object `"YYYY-MM-DD"` → integer minutes | sparse; BR5 skips short days, BR15 skips unfinished days, BR26 skips absence days |
| `TotalHours` | `totalMinutes` | integer minutes | BR16 "Total hours for the month" column |
| `AbsenceDays` | `absenceDays` | array of `"YYYY-MM-DD"` | **BR26** — its own column on the table; distinct calendar dates, time part always dropped |

### AbsenceDto (`src/WorkTime.Application/Dto/AbsenceDto.cs`) — response of endpoint 11

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | BR28: changing a status reuses the id, so history stays attached to one record per day |
| `UserId` | `userId` | string uuid | employee the status belongs to |
| `Date` | `date` | `"YYYY-MM-DD"` | |
| `Reason` | `reason` | enum token | BR25 reason |
| `CreatedByUserId` | `createdByUserId` | string uuid \| null | BR27 tooltip: whom the employee must contact to clear the status |

### UserDto (`src/WorkTime.Application/Dto/UserDto.cs`)

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | |
| `Login` | `login` | string | |
| `FullName` | `fullName` | string | BR16 row label source |
| `Role` | `role` | enum token | BR19 roles; drives every access-rights rule |
| `DepartmentId` | `departmentId` | string uuid | BR23.1: period closures are configured per department |
| `IsActive` | `isActive` | boolean | BR24: deactivated ⇒ data kept, access blocked |
| `FailedLoginAttempts` | `failedLoginAttempts` | integer | BR35 counter (visible for the countdown UI) |
| `LockedUntil` | `lockedUntil` | timestamp \| null | BR35 lock window end |
| `Locked` | `locked` | boolean | BR35: blocked until end of day after 10 failures |

**Deliberately absent:** `PasswordHash` — BR34 (hashed storage) plus "no reason for it to
leave the system". Any login form must not expect a password field in any response DTO.

### DepartmentDto (`src/WorkTime.Application/Dto/DepartmentDto.cs`)

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | |
| `Name` | `name` | string | |
| `ParentId` | `parentId` | string uuid \| null | null ⇒ top level; BR23.1 closures are per department, so the tree is part of the handover |

### PeriodClosureDto (`src/WorkTime.Application/Dto/PeriodClosureDto.cs`)

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | endpoint 9 query `periodClosureId` |
| `DepartmentId` | `departmentId` | string uuid | BR23.1 — added in the fix round; this is what makes "active closure of this department" answerable |
| `Year` / `Month` | `year` / `month` | integer | the calendar month the report window covers (BR16/BR18) |
| `IsClosed` | `isClosed` | boolean | BR23.1 state |
| `ClosedAt` | `closedAt` | timestamp \| null | |
| `ClosedBy` | `closedBy` | string uuid \| null | administrator who closed it |
| `ReopenReason` | `reopenReason` | string \| null | BR23.2 mandatory reason / act number |
| `ReopenedByUserId` | `reopenedByUserId` | string uuid \| null | BR23.2 "Emergency correction" author |

### AuditLogDto (`src/WorkTime.Application/Dto/AuditLogDto.cs`) — **RESERVED · Stage 2** transport (BR29 admin-only)

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | |
| `UserId` | `userId` | string uuid | BR30 author — proven by the BR3 auto-finish test |
| `Action` | `action` | enum token | |
| `EntityType` | `entityType` | string | `"WorkDay"`, `"Break"`, `"Report"`, … |
| `EntityId` | `entityId` | string uuid | the entity that changed (read path: `GetHistoryForEntityAsync`) |
| `OldValue` / `NewValue` | `oldValue` / `newValue` | string \| null | BR30 previous/new value; auto-finish writes `"Auto-finish: EndTime = 23:59:59"` |
| `Timestamp` | `timestamp` | timestamp | |

### CommentDto (`src/WorkTime.Application/Dto/CommentDto.cs`) — **RESERVED · Stage 2** (BR13, no service yet)

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `Id` | `id` | string uuid | |
| `ReportId` | `reportId` | string uuid | report being commented on |
| `ManagerId` | `managerId` | string uuid | author |
| `Text` | `text` | string | |
| `CreatedAt` | `createdAt` | timestamp | BR13: visible to manager + administrator only, never to an employee row |

### AuthResult (`src/WorkTime.Application/Dto/AuthResult.cs`) — response of endpoint 1

| Field (C#) | JSON key | JSON type | Notes |
| --- | --- | --- | --- |
| `IsSuccess` | `isSuccess` | boolean | false never means "throw" — login failures are data, not errors |
| `RequiresCaptcha` | `requiresCaptcha` | boolean | BR36: from the 2nd wrong password on (manager/admin only) |
| `IsLocked` | `isLocked` | boolean | BR35 lock/blocked, and BR24 deactivated account |
| `Message` | `message` | string | display-only, `[RU]` placeholder; empty on success |
| `LockedUntil` | `lockedUntil` | timestamp \| null | BR35 countdown target for the UI |

**RESERVED · Stage 2:** on `isSuccess = true` the response gains
`{ "accessToken": "<jwt>", "expiresIn": <seconds> }`. Nothing about token claims exists in
Stage 1 code, so do not invent claim names — they are decided with the controllers.

---

## 6. NOT part of the contract

**There is no `employeeIds` list anywhere.** `GetMonthlyReportAsync(periodClosureId,
departmentId, ct)` resolves the rows itself through
`IUserRepository.GetActiveEmployeesInDepartmentAsync(departmentId, ct)`. The old
parameterless-list signature that a caller could fill with ids was removed in the fix round;
any frontend code that assembles an employee list is off-contract by construction. The
"addressing rule" for every collection endpoint: **the client sends criteria
(id / date / period / department), never a list of ids.**

Endpoint 9 window resolution (`ReportService.TryGetClosureForAsync`):

| Sent | Meaning | Result |
| --- | --- | --- |
| `?periodClosureId=<uuid>` | explicit billing-period closure | its `Year`/`Month` is the window |
| `?departmentId=<uuid>` | that department's **active** (not closed) closure | its `Year`/`Month` is the window |
| neither (`Guid.Empty`) | every active user of the system, but **no window** | `[]` — a window always has to come from a closure or a department |

Other things that are not on the wire: repository ids assembled by clients; cancellationToken
plumbing (server-side only); `[RU]` message text used for logic; `PasswordHash`; anything
named "…" marked RESERVED above.

---

## 7. Worked JSON examples (real values from the green tests)

The times, names and totals below are the exact inputs/assertions of passing tests; UUIDs are
illustrative because the tests generate them with `Guid.NewGuid()`.

### 7.1 `DailyReport` — `GET /api/reports/daily/{workDayId}`

From `ReportServiceTests.GetDailyReport_*` (day 08:00–16:00, counted break 12:00–13:00,
BR8-excluded break 14:00–14:02 ⇒ both listed, only the counted one subtracted:
`totalMinutes == 420`):

```json
{
  "startTime": "08:00",
  "endTime": "16:00",
  "breaks": [
    { "startTime": "12:00", "endTime": "13:00" },
    { "startTime": "14:00", "endTime": "14:02" }
  ],
  "totalMinutes": 420
}
```

### 7.2 `MonthlyReport` — `GET /api/reports/monthly?departmentId=<uuid>` (element of the array)

From `ReportServiceTests.GetMonthlyReport_ListsAbsenceDaysAndExcludesTheirHours`: employee
"Ivan Petrov" worked 8 h on 2024-03-01 and 6 h on 2024-03-04; the manager set an absence
(`"Leave"`) for 2024-03-04, so that day contributes **no hours** and appears in `absenceDays`
instead — assertions `totalMinutes == 480`, `hoursByDay.Count == 1`,
`!hoursByDay["2024-03-04"]`, `absenceDays.Count == 1`:

```json
{
  "employeeName": "Ivan Petrov",
  "hoursByDay": {
    "2024-03-01": 480
  },
  "totalMinutes": 480,
  "absenceDays": ["2024-03-04"]
}
```

Second real variant — `GetMonthlyReport_SubtractsCountedBreaksFromEveryDay`: one day
08:00–16:00 with a counted break 12:00–13:00 ⇒ the monthly table uses the same formula as the
daily report (BR4 + BR11), so the day cell and the total are 7 h, not 8 h:

```json
{
  "employeeName": "Ivan Petrov",
  "hoursByDay": { "2024-03-01": 420 },
  "totalMinutes": 420,
  "absenceDays": []
}
```

And the BR15/BR5 variant (`GetMonthlyReport_SkipsIncompleteDaysAndDaysUnderFifteenMinutes`):
a 10-minute day (09:00–09:10) and a started-but-never-finished day produce no keys at all —
`hoursByDay` holds only `{"2024-03-01": 480}`.

### 7.3 `AuthResult` — `POST /api/auth/login`

Success (`Login_SuccessfulLoginResetsFailuresAndLockState`, login `"manager"`, correct
password; the stored user is left with `failedLoginAttempts = 0`, `locked = false`,
`lockedUntil = null`):

```json
{ "isSuccess": true, "requiresCaptcha": false, "isLocked": false, "message": "", "lockedUntil": null }
```

CAPTCHA countdown (`Login_CaptchaRequiredFromSecondWrongPasswordOn`, second wrong password of
a **Manager** — `failedLoginAttempts == 2`, countdown = `5 − 2`):

```json
{
  "isSuccess": false,
  "requiresCaptcha": true,
  "isLocked": false,
  "message": "[RU] Wrong password. CAPTCHA required. 3 attempt(s) before the lock.",
  "lockedUntil": null
}
```

Fifteen-minute lock (`Login_FifthWrongPasswordLocksAccountForFifteenMinutes`, fifth wrong
password of an **Admin**, `now = 2024-03-10T10:00:00` ⇒ `lockedUntil = now + 15 min`):

```json
{
  "isSuccess": false,
  "requiresCaptcha": false,
  "isLocked": true,
  "message": "[RU] Account is locked for 15 minutes.",
  "lockedUntil": "2024-03-10T10:15:00"
}
```

Employee with five wrong passwords (`Login_EmployeeIsNeverLockedOrAskedForCaptcha`): BR35/BR36
apply to managers and administrators only, so the body is
`{ "isSuccess": false, "requiresCaptcha": false, "isLocked": false, "message": "[RU] Wrong password.", "lockedUntil": null }`
while the stored user still shows `failedLoginAttempts = 5`.

### 7.4 A business-rule rejection (real message) — endpoint 2, BR1

`WorkTimeServiceTests.StartWorkDay_SecondOpenDayThrowsBusinessRuleException`: day already open
on `2024-03-10`, second "Start work" ⇒ HTTP `409`:

```json
{
  "error": {
    "code": "BR1",
    "message": "[RU] Work day already open for this employee - finish it before starting a new one (BR1).",
    "httpStatus": 409
  }
}
```

Endpoint 8, BR21 (`GetDailyReport_EmployeeAsksForAnotherEmployeeReportThrowsBusinessRuleException`):
an `"Employee"` requesting another employee's `workDayId` ⇒ `409`,
`{ "code": "BR21", "message": "[RU] You may see only your own report." }`.

---

## 8. Reserved for Stage 2 / Stage 4 (do not build against yet)

| Rule | What is missing now | Where it lands |
| --- | --- | --- |
| **BR13** comments | no service/controller at all; `CommentDto` exists only as a DTO | Stage 2 — endpoint 12, Manager/Admin visibility filter |
| **BR17** export gate (+ BR18 custom period) | no Excel export method, no role check for it | Stage 2 — endpoint 13 |
| **BR23.1 / BR23.2** close & reopen billing period | the **data model is ready** (`PeriodClosure.DepartmentId`, `IsClosed`, `ClosedAt/ClosedBy`, `ReopenReason/ReopenedByUserId`) but no writer service exists — nothing closes or reopens a period today | Stage 2 — endpoint 14 (+ audit "Emergency correction") |
| BR31 confirmations | no confirmation handshake in any request/response shape | Stage 2 (finish day, delete break, change times after the day) |
| BR32 session expiry (8 h) / re-login | no session machinery; `InfrastructureLayer.cs` is a comment-only placeholder | Stage 2 — JWT lifetime + refresh policy |
| BR33 daily 03:00 backup, 30-day retention | no backup machinery | Stage 4 (Infrastructure + real DB) |
| **BR34** hashed passwords | `AuthService.HashPasswordPlaceholder` returns the password unchanged (`TODO`) | Stage 4 — Infrastructure hasher; DTO already excludes `PasswordHash` |
| BR35 per-**day** attempt counter | counter is a plain running total on `User`, not reset per calendar day | Stage 2/4 with the repository layer |
| BR36 CAPTCHA verification | `requiresCaptcha` is only a flag; no challenge/response endpoint, no verification | Stage 2 |
| BR29/BR30 audit log read path | only `AutoFinishWorkDayAsync` writes an entry; break/work-day/report/absence edits are not logged yet; repository read exists (`GetHistoryForEntityAsync`) but no service uses it | Stage 2 — logging in write paths + endpoint 15 |ФС
| Endpoint 2 identity gap | `StartWorkDayAsync` does not resolve the user, so an unknown `employeeId` creates a day instead of failing; the BR27/BR1 lookups are honest now but the actor is unvalidated | Stage 2 — controller validates JWT subject before calling |

---

## 9. How to extend this contract without breaking it

1. Add the DTO field in `src/WorkTime.Application/Dto` **first** (a DTO is the frontend's
   view; nothing else reaches the client).
2. Add the service method that fills it, then a test in `tests/WorkTime.Tests` asserting the
   exact value — the sample payloads above are only allowed to exist for values a green test
   asserts.
3. Update the matching row here in the same commit; if a rule starts/stops throwing, change
   the "Business rules enforced" column and the error table, not prose elsewhere.

Version note: this file reflects the state **after** the Stage-1 fix round (`PeriodClosure.DepartmentId`,
`MonthlyReport.AbsenceDays`, `IDepartmentRepository`, BR21 daily-report gate, monthly break
subtraction, date-scoped BR1 guard, no caller-supplied id lists).
---

## 10. Этап 2: фактические эндпоинты (дополнение к документу)

Раздел описывает то, что сейчас реально работает в `src/WorkTime.Api/Controllers`.
Формы запросов и ответов из §1–§7 **не изменены**: они остаются действующими и ниже
только дополняются новыми эндпоинтами. Идентификаторы, отмеченные звёздочкой в §2
(`employeeId`, `currentUserId`, `editorId`, `actorId`), с этапа 2 берутся из subject
Bearer-токена и в теле запроса больше не передаются.

Политики доступа (`AuthorizationPolicies.cs`): `AdminOnly` = только администратор,
`ManagerOrAdmin` = руководитель или администратор, `EmployeeOnly` = любой
аутентифицированный сотрудник. `DefaultPolicy` и `FallbackPolicy` требуют
аутентификации, поэтому эндпоинт без явной политики тоже закрыт для анонимных
запросов; анонимен только `POST /api/auth/login` (`[AllowAnonymous]`).
Все контроллеры объявлены как `[ApiController]`, поэтому невалидное тело или
параметр автоматически становится 400 с формой `VALIDATION` (§12);
`[ProducesResponseType]` описывает часть кодов — таблица перечисляет коды, которые
реально возникают в коде.

| № | Метод и путь | Роль / политика | Bearer | Тело / параметры запроса | Успешный ответ | Коды ошибок |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `POST /api/auth/login` | анонимный (`AllowAnonymous`) | не нужен | `{ login, password }` (§2 строка 1) | `LoginWire` + `accessToken`, `expiresIn` при успехе (§5) | 400 `FIELD_MISSING` / `VALIDATION`, 404 `ENTITY_NOT_FOUND` (`User`) |
| 2 | `POST /api/work-days/start` | аутентифицированный (`DefaultPolicy`) | нужен | `{ date, startTime }` | `WorkDayWire` | 400 `VALIDATION` / `DATE_INVALID` / `TIME_INVALID` / `IDENTITY_MISSING` / `IDENTITY_INVALID`, 404 `ENTITY_NOT_FOUND`, 409 `BR1`, `BR27` |
| 3 | `POST /api/work-days/{workDayId}/finish` | аутентифицированный | нужен | `{ endTime }` | `WorkDayWire` | 400 `VALIDATION` / `TIME_INVALID`, 404 `WorkDay`, 409 `BR2`, `BR21` |
| 4 | `GET /api/work-days/current` | аутентифицированный | нужен | нет параметров | `WorkDayWire` или `null` (открытой смены нет — это 200 с `null`, не 404) | 400 `IDENTITY_MISSING` / `IDENTITY_INVALID`, 404 `User` |
| 5 | `POST /api/work-days/{workDayId}/auto-finish` | аутентифицированный | нужен | нет тела (`employeeId` из токена) | `WorkDayWire` | 400 `IDENTITY_MISSING`, 404 `WorkDay`, 409 `BR2` |
| 6 | `POST /api/work-days/{workDayId}/breaks/start` | аутентифицированный | нужен | `{ startTime }` | `BreakWire` | 400 `VALIDATION` / `TIME_INVALID`, 404 `WorkDay`, 409 `BR6`, `BR10`, `BR21` |
| 7 | `POST /api/breaks/{breakId}/finish` | аутентифицированный | нужен | `{ endTime }` | `BreakWire` | 400 `VALIDATION` / `TIME_INVALID`, 404 `Break` / `WorkDay`, 409 `BR8`, `BR10`, `BR6` |
| 8 | `POST /api/reports/{workDayId}` | аутентифицированный | нужен | `{ text }` | `ReportWire` | 400 `VALIDATION` / `FIELD_MISSING`, 404 `WorkDay` / `User`, 409 `BR12`, `BR14`, `BR21` |
| 9 | `GET /api/reports/daily/{workDayId}` | аутентифицированный | нужен | путь: `workDayId` | `DailyReportWire` или `null` (§2 строка 8) | 400 `IDENTITY_MISSING`, 404 `WorkDay` / `User`, 409 `BR21` |
| 10 | `GET /api/reports/monthly` | `ManagerOrAdmin` | нужен | query `periodClosureId?`, `departmentId?` (§6) | `MonthlyReportWire[]` | 400 `VALIDATION` (query не распарсился) |
| 11 | `GET /api/work-days/{employeeId}/can-start` | аутентифицированный | нужен | query `date`; `employeeId` обязан совпадать с subject токена | `{ "canStart": true|false }` | 400 `DATE_INVALID` / `IDENTITY_MISSING`, 404 `User`, 409 `BR21` (чужой `employeeId`) |
| 12 | `POST /api/absences/{employeeId}` | `ManagerOrAdmin` | нужен | `{ date, reason }` (`actorId` из токена) | `AbsenceWire` | 400 `DATE_INVALID` / `REASON_INVALID` / `IDENTITY_MISSING`, 404 `User`, 409 `BR25` |
| 13 | `PUT /api/absences/{employeeId}` | `ManagerOrAdmin` | нужен | та же форма, что строка 12 | `AbsenceWire` | те же коды, что строка 12 |
| 14 | `GET /api/absences/{employeeId}?date=` | аутентифицированный | нужен | query `date` | `AbsenceWire[]` (`[]`, когда статусов нет) | 400 `DATE_INVALID` / `IDENTITY_MISSING`, 403 (чужой `employeeId` для сотрудника — тело пустое) |
| 15 | `GET /api/absences?from=&to=` | `ManagerOrAdmin` | нужен | query `from`, `to` (§3: `YYYY-MM-DD`) | `AbsenceWire[]` | 400 `DATE_INVALID` / `IDENTITY_MISSING` |
| 16 | `POST /api/reports/{workDayId}/comments` | `ManagerOrAdmin` | нужен | `{ reportId, text }` (§2 строка 12) | `CommentWire` | 400 `REPORT_ID_REQUIRED` / `REPORT_WORK_DAY_MISMATCH` / `FIELD_MISSING` / `VALIDATION`, 404 `Report` / `User` |
| 17 | `GET /api/reports/{workDayId}/comments` | `ManagerOrAdmin` | нужен | путь: `workDayId` | `CommentWire[]` (`[]` когда комментариев нет) | 403 (роль не подходит); других кодов эндпоинт не объявляет |
| 18 | `GET /api/reports/monthly/export` | `ManagerOrAdmin` | нужен | query как строка 10 | тело — CSV, `Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="monthly-report.csv"` | 400 `VALIDATION` |
| 19 | `GET /api/reports/monthly/export.xlsx` | `ManagerOrAdmin` | нужен | нет параметров | **501** `ProblemDetails` (см. §12) — Excel-экспорт появится на этапе 4 | 501 `EXPORT_NOT_IMPLEMENTED` |
| 20 | `GET /api/users?name=&includeInactive=` | `ManagerOrAdmin` | нужен | query `name` (фильтр по имени), `includeInactive` (по умолчанию `false` — деактивированные не показываются, BR24) | `UserWire[]` | 403 (роль не подходит); subject не читается |
| 21 | `GET /api/users/{userId}` | `ManagerOrAdmin` | нужен | путь: `userId` | `UserWire` | 404 `User`, 403 (роль не подходит) |
| 22 | `POST /api/users` | `AdminOnly` | нужен | `{ login, fullName, password, role, departmentId }` (§11.1) | `UserWire` | 400 `FIELD_MISSING` / `ROLE_INVALID` / `DEPARTMENT_ID_REQUIRED` / `VALIDATION`, 404 `Department`, 409 `USER_LOGIN_TAKEN` |
| 23 | `PUT /api/users/{userId}` | `AdminOnly` | нужен | `{ fullName?, role?, departmentId?, isActive? }` (§11.2) | `UserWire` | 400 `FIELD_MISSING` / `ROLE_INVALID` / `VALIDATION`, 404 `User`, 409 `BR20` |
| 24 | `DELETE /api/users/{userId}` | `AdminOnly` | нужен | путь: `userId` | 200, тело пустое | 400 `IDENTITY_MISSING`, 404 `User`, 409 `BR20` |
| 25 | `POST /api/users/{userId}/deactivate` | `AdminOnly` | нужен | нет тела | `UserWire` (`isActive = false`, данные и история сохранены — BR24) | 400 `IDENTITY_MISSING`, 404 `User`, 409 `BR20` |
| 26 | `POST /api/users/{userId}/unlock` | `AdminOnly` | нужен | нет тела | `UserWire` (`failedLoginAttempts = 0`, `locked = false`, `lockedUntil = null` — BR35) | 400 `IDENTITY_MISSING`, 404 `User` |
| 27 | `GET /api/departments` | `ManagerOrAdmin` | нужен | нет параметров | `DepartmentWire[]` | 403 (роль не подходит); subject не читается |
| 28 | `GET /api/departments/{departmentId}` | `ManagerOrAdmin` | нужен | путь: `departmentId` | `DepartmentWire` | 404 `Department`, 403 (роль не подходит) |
| 29 | `GET /api/departments/{departmentId}/children` | `ManagerOrAdmin` | нужен | путь: `departmentId` | `DepartmentWire[]` (`[]` у листового отдела) | 404 `Department`, 403 (роль не подходит) |
| 30 | `POST /api/departments` | `AdminOnly` | нужен | `{ name, parentId? }` (§11.3) | `DepartmentWire` | 400 `FIELD_MISSING` / `VALIDATION`, 404 `Department` (нет такого родителя) |
| 31 | `PUT /api/departments/{departmentId}` | `AdminOnly` | нужен | `{ name, parentId? }` (§11.3) | `DepartmentWire` | 400 `FIELD_MISSING` / `SELF_PARENT` / `VALIDATION`, 404 `Department` |
| 32 | `DELETE /api/departments/{departmentId}` | `AdminOnly` | нужен | путь: `departmentId` | 200, тело пустое | 400 `IDENTITY_MISSING`, 404 `Department`, 409 `DEPARTMENT_HAS_CHILDREN`, 409 `DEPARTMENT_HAS_EMPLOYEES` |
| 33 | `GET /api/audit-logs?entityId=` | `AdminOnly` (BR29) | нужен | query `entityId` — обязателен (§6: клиент sends критерий, а не список) | `AuditLogWire[]` | 400 `ENTITY_ID_MISSING` / `IDENTITY_MISSING` |
| 34 | `POST /api/period-closures/{periodClosureId}/close` | `AdminOnly` | нужен | нет тела | `PeriodClosureWire` (`isClosed = true`, `closedAt`, `closedBy`) | 400 `IDENTITY_MISSING`, 404 `PeriodClosure`, 409 `BR23.1` (уже закрыт) |
| 35 | `POST /api/period-closures/{periodClosureId}/reopen` | `AdminOnly` | нужен | `{ reason }` — обязательно (BR23.2) | `PeriodClosureWire` (`isClosed = false`, `reopenReason`, `reopenedByUserId`) | 400 `FIELD_MISSING` / `VALIDATION`, 404 `PeriodClosure`, 409 `BR23.2` (период не закрыт) |

Чтение аудита и закрытие/вскрытие периода объявлены на путях с ведущим `/`
(`GET /api/audit-logs`, `POST /api/period-closures/...`), поэтому они не зависят от
префикса контроллера `api/admin`.

Swagger/OpenAPI включён на этапе 2: документ `GET /swagger/v1/swagger.json` и Swagger UI
`/swagger` остаются анонимными (это middleware, не MVC-эндпоинты), а требование Bearer
добавляется автоматически ко всем эндпоинтам без `[AllowAnonymous]`. Это дополнение к
§1, где на этапе 1 OpenAPI сознательно не было.

---

## 11. Формы запросов и ответов новых эндпоинтов

Скалярные форматы те же, что §3; ключи — camelCase, как в таблицах §5.

### 11.1 `POST /api/users`

Запрос (пароль приходит только в запросе и никогда не возвращается — BR34):

```json
{
  "login": "ivan.petrov",
  "fullName": "Ivan Petrov",
  "password": "s3cret-pass",
  "role": "Employee",
  "departmentId": "0e5d6a7b-1122-3344-5566-778899001122"
}
```

Ответ `200` — `UserWire` из §5 (та же таблица полей, `passwordHash` отсутствует):

```json
{
  "id": "9b2f4c1d-0a1b-2c3d-4e5f-60718293a4b5",
  "login": "ivan.petrov",
  "fullName": "Ivan Petrov",
  "role": "Employee",
  "departmentId": "0e5d6a7b-1122-3344-5566-778899001122",
  "isActive": true,
  "failedLoginAttempts": 0,
  "lockedUntil": null,
  "locked": false
}
```

### 11.2 `PUT /api/users/{userId}`

Все поля необязательны; пропущенное поле означает «не менять». `isActive: false`
эквивалентно эндпоинту деактивации (BR24), `isActive: true` возвращает доступ:

```json
{ "fullName": "Ivan Petrov Jr.", "role": "Manager", "isActive": true }
```

Ответ — обновлённый `UserWire`.

### 11.3 Отделы

`POST /api/departments` и `PUT /api/departments/{departmentId}` принимают:

```json
{ "name": "Toll A", "parentId": null }
```

`parentId: null` — вершина дерева (§5, `DepartmentDto.ParentId`). Ответ — `DepartmentWire`:

```json
{ "id": "1a2b3c4d-5566-7788-99aabbccddee00", "name": "Toll A", "parentId": null }
```

`GET /api/departments/{departmentId}/children` возвращает массив `DepartmentWire`
(пустой массив у листового отдела).

### 11.4 Комментарии к отчёту (BR13)

`POST /api/reports/{workDayId}/comments` — тело из §2 строка 12, плюс путь `workDayId`;
сервер сверяет, что отчёт принадлежит этому рабочему дню (иначе
`REPORT_WORK_DAY_MISMATCH`, 400):

```json
{ "reportId": "7c8d9e0f-1122-3344-5566-778899001122", "text": "[RU] Check the totals for 2024-03-01." }
```

Ответ — `CommentWire` (§5); автор `managerId` берётся из subject токена:

```json
{
  "id": "2b3c4d5e-6677-8899-aabb-cddeff001122",
  "reportId": "7c8d9e0f-1122-3344-5566-778899001122",
  "managerId": "9b2f4c1d-0a1b-2c3d-4e5f-60718293a4b5",
  "text": "[RU] Check the totals for 2024-03-01.",
  "createdAt": "2026-11-03T17:41:01"
}
```

`GET /api/reports/{workDayId}/comments` — массив таких же объектов; виден
руководителю и администратору, сотруднику — 403 (BR13).

### 11.5 Журнал изменений (BR29/BR30)

`GET /api/audit-logs?entityId=<uuid>` — ответ §5 `AuditLogWire[]`:

```json
[
  {
    "id": "3c4d5e6f-7788-99aa-bbcc-ddeeff001122",
    "userId": "9b2f4c1d-0a1b-2c3d-4e5f-60718293a4b5",
    "action": "Update",
    "entityType": "WorkDay",
    "entityId": "5a1c9b0e-2f31-4a6d-9b7c-0d2e4f60a8b1",
    "oldValue": "endTime=null",
    "newValue": "Auto-finish: EndTime = 23:59:59",
    "timestamp": "2026-11-03T23:59:59"
  }
]
```

Без `entityId` (или `entityId=00000000-0000-0000-0000-000000000000`) — 400
`ENTITY_ID_MISSING`: список записей клиент не передаёт (§6).

### 11.6 Расчётный период (BR23.1 / BR23.2)

`POST /api/period-closures/{periodClosureId}/close` — тело не требуется, ответ —
`PeriodClosureWire` (§5):

```json
{
  "id": "4d5e6f70-8899-aabb-cdde-eeff00112233",
  "departmentId": "1a2b3c4d-5566-7788-99aabbccddee00",
  "year": 2024,
  "month": 3,
  "isClosed": true,
  "closedAt": "2026-11-03T18:00:00",
  "closedBy": "9b2f4c1d-0a1b-2c3d-4e5f-60718293a4b5",
  "reopenReason": null,
  "reopenedByUserId": null
}
```

`POST /api/period-closures/{periodClosureId}/reopen` — причина/номер акта обязателен
(BR23.2, «Экстренная корректировка»):

```json
{ "reason": "Act #12 dated 2026-11-03" }
```

Ответ — тот же `PeriodClosureWire` с `isClosed = false`, `reopenReason` и
`reopenedByUserId`. Вскрытие незакрытого периода — 409 `BR23.2`.

### 11.7 Кнопка «Начать рабочий день» (BR27)

`GET /api/work-days/{employeeId}/can-start?date=YYYY-MM-DD` — ответ ровно:

```json
{ "canStart": false }
```

`false` — BR27 как состояние кнопки (отсутствие, чужой день, уже открытый день);
исключение при этом не бросается. `employeeId` обязан совпадать с subject токена —
чужой id даёт 409 `BR21`.

### 11.8 Экспорт месячной таблицы (BR17/BR18)

`GET /api/reports/monthly/export?periodClosureId=<uuid>` (или `departmentId=<uuid>`,
как строка 10) отдаёт **не JSON**: `200`, `Content-Type: text/csv; charset=utf-8`,
`Content-Disposition: attachment; filename="monthly-report.csv"`, тело — CSV-таблица
по тем же строкам/колонкам, что `MonthlyReportWire` (§5).

`GET /api/reports/monthly/export.xlsx` на этапе 2 возвращает 501 (см. §12), чтобы
клиент не получил молча пустой файл; тот же текст/CSV доступен через `/export`.

---

## 12. Формат ошибок: ProblemDetails (RFC 7807)

Ошибки — `application/problem+json`. Четыре стандартных члена RFC 7807
(`type`, `title`, `status`, `detail`) дополнены служебными ключами, которые
перенесены из §1: `code`, `message`, `httpStatus`, а для 404 — `entityType` и
`entityId`. Старый envelope `error { code, message, httpStatus }` остаётся в теле
**рядом** с членами RFC 7807, поэтому код по-прежнему читается как `error.code`
(или как `code`) и человеческое сообщение не парсится.

Бизнес-правило (409):

```json
{
  "type": "https://errors.worktime/business-rule",
  "title": "Business rule violated",
  "status": 409,
  "detail": "[RU] Work day already open for this employee - finish it before starting a new one (BR1).",
  "code": "BR1",
  "message": "[RU] Work day already open for this employee - finish it before starting a new one (BR1).",
  "httpStatus": 409,
  "error": {
    "code": "BR1",
    "message": "[RU] Work day already open for this employee - finish it before starting a new one (BR1).",
    "httpStatus": 409
  }
}
```

Сущность не найдена (404):

```json
{
  "type": "https://errors.worktime/entity-not-found",
  "title": "Entity not found",
  "status": 404,
  "detail": "[RU] No such working day.",
  "code": "ENTITY_NOT_FOUND",
  "entityType": "WorkDay",
  "entityId": "5a1c9b0e-2f31-4a6d-9b7c-0d2e4f60a8b1",
  "message": "[RU] No such working day.",
  "httpStatus": 404,
  "error": { "code": "ENTITY_NOT_FOUND", "message": "[RU] No such working day.", "httpStatus": 404 }
}
```

Непарсированное/невалидное тело или параметр (400). Валидация
DataAnnotations (`[Required]`, `[RegularExpression]`) идёт через
`InvalidModelStateResponseFactory`, поэтому её форма совпадает с ручной:

```json
{
  "type": "https://errors.worktime/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "[RU] Request validation failed. date: [RU] Field must be a date in YYYY-MM-DD form.",
  "code": "VALIDATION",
  "message": "[RU] Request validation failed. date: [RU] Field must be a date in YYYY-MM-DD form.",
  "httpStatus": 400,
  "error": { "code": "VALIDATION", "message": "...", "httpStatus": 400 }
}
```

Сессия истекла по BR32 (401, с `WWW-Authenticate: Bearer`):

```json
{
  "type": "https://errors.worktime/session-expired",
  "title": "Session expired",
  "status": 401,
  "detail": "[RU] More than 8 hours passed since the last activity of this user (BR32).",
  "code": "SESSION_EXPIRED",
  "message": "...",
  "httpStatus": 401,
  "error": { "code": "SESSION_EXPIRED", "message": "...", "httpStatus": 401 }
}
```

Нереализованный экспорт (501) — служебные ключи здесь те же, но `type` отличается
от остальных (его base — `https://worktime/api/errors/...`), `title` и `detail`
человекочитаемы:

```json
{
  "type": "https://worktime/api/errors/export-not-implemented",
  "title": "[RU] Excel export is not implemented yet (BR17, Stage 4).",
  "status": 501,
  "detail": "[RU] Use GET /api/reports/monthly/export for the CSV form of the same data.",
  "code": "EXPORT_NOT_IMPLEMENTED",
  "httpStatus": 501
}
```

Неожиданная ошибка сервера (500) наружу отдаёт только стабильный код:

```json
{
  "type": "https://errors.worktime/internal-error",
  "title": "The request could not be completed",
  "status": 500,
  "detail": "[RU] The request could not be completed.",
  "code": "INTERNAL_ERROR",
  "message": "[RU] The request could not be completed.",
  "httpStatus": 500,
  "error": { "code": "INTERNAL_ERROR", "message": "[RU] The request could not be completed.", "httpStatus": 500 }
}
```

### 12.1 Коды статуса и их смысл

| Status | Когда | `type` | `code` |
| --- | --- | --- | --- |
| 400 | синтаксис запроса (тело, query, time/date, роль, отсутствующий id) | `…/validation`, `…/invalid-request` | `VALIDATION`, `FIELD_MISSING`, `DATE_INVALID`, `TIME_INVALID`, `REASON_INVALID`, `ROLE_INVALID`, `DEPARTMENT_ID_REQUIRED`, `REPORT_ID_REQUIRED`, `REPORT_WORK_DAY_MISMATCH`, `SELF_PARENT`, `ENTITY_ID_MISSING`, `IDENTITY_MISSING`, `IDENTITY_INVALID`, `ROLE_MISSING` |
| 401 | нет токена / токен невалиден; сессия истекла по BR32 | анонимный запрос — тело пустое + `WWW-Authenticate: Bearer`; BR32 — `…/session-expired` | `SESSION_EXPIRED` (и код `UnauthorizedRequestException`, если он бросается) |
| 403 | аутентификация есть, роль не подходит под политику; чужой `employeeId`/отчёт для сотрудника (BR13, BR27) | тело пустое (MVC-авторизация, без ProblemDetails) | — |
| 404 | сущность не найдена | `…/entity-not-found` | `ENTITY_NOT_FOUND` + `entityType` (`User`, `WorkDay`, `Break`, `Report`, `Department`, `PeriodClosure`) |
| 409 | бизнес-правило нарушено | `…/business-rule` | `BR1`, `BR2`, `BR6`, `BR8`, `BR10`, `BR12`, `BR14`, `BR20`, `BR21`, `BR23.1`, `BR23.2`, `BR25`, `BR27`, `USER_LOGIN_TAKEN`, `DEPARTMENT_HAS_CHILDREN`, `DEPARTMENT_HAS_EMPLOYEES` |
| 500 | необработанная ошибка | `…/internal-error` | `INTERNAL_ERROR` |
| 501 | Excel-экспорт ещё не построен (этап 4) | `https://worktime/api/errors/export-not-implemented` | `EXPORT_NOT_IMPLEMENTED` |

Важно: 401 от анонимного запроса и 403 от политики — пустое тело (так отдаёт
ASP.NET Core); ProblemDetails там не появляется. Клиент различает «не аутентифицирован»
(401) и «аутентифицирован, но право не то» (403) по статусу, а не по телу.

---

## 13. Авторизация: Bearer JWT (этап 2)

- Схема: `Authorization: Bearer <jwt>`; подписание **HS256**, ключ —
  `JwtSettings.SecretKey` (минимум 32 байта; вне Development значения приходят из
  конфигурации, dev-дефолт разрешён только в Development).
- `iss` = `JwtSettings.Issuer` (по умолчанию `WorkTime.Api`), `aud` =
  `JwtSettings.Audience` (по умолчанию `WorkTime.Client`). Оба проверяются при валидации.
- Claims ровно три (`TokenService`): subject (`ClaimTypes.NameIdentifier`) — Guid
  пользователя; `ClaimTypes.Role` — токен роли из §4 (`Admin` / `Manager` /
  `Employee`); `ClaimTypes.Name` — логин для отображения и логов, в решениях не участвует.
  Никаких других claims нет; их имена не выдумывать.
- `expiresIn` в ответе логина — секунды до истечения токена:
  `JwtSettings.ExpiryMinutes * 60`, по умолчанию `28800` (8 часов).
- Валидация токена: issuer, audience, срок, signing key; допуск часового дрейфа —
  1 минута (`JwtBearerSetup.ClockSkew`).
- Идентификаторы (`employeeId`, `currentUserId`, `editorId`, `actorId`) берутся из
  subject; тело запроса их больше не содержит (§2, звёздочки). Токен без subject или с
  нечитаемым subject — 400 `IDENTITY_MISSING` / `IDENTITY_INVALID`, а не 401: это
  испорченный токен, а не отсутствие аутентификации.
- Политики: `AdminOnly`, `ManagerOrAdmin`, `EmployeeOnly`; `DefaultPolicy` и
  `FallbackPolicy` = «только аутентифицированный», поэтому эндпоинт без политики тоже
  закрыт. Анонимен только `POST /api/auth/login`.
- Базовая линия активности ставится при успешном логине (BR32), токен — носитель
  идентичности; его срок и срок сессии считаются отдельно (см. §14).

---

## 14. BR32: 8 часов неактивности

- Лимит = `JwtSettings.ExpiryMinutes`, по умолчанию **480 минут (8 часов)**; он же
  срок жизни токена.
- Учёт ведётся in-memory по subject токена (`UserActivityTracker`), один экземпляр на
  процесс: успешный `POST /api/auth/login` ставит базовую линию, каждый
  аутентифицированный запрос продлевает её.
- Если с последней активности прошло **больше** лимита, middleware отказывает запрос
  **до** контроллера: `401` + `WWW-Authenticate: Bearer` + ProblemDetails с
  `code = SESSION_EXPIRED` (§12). Это отказ сессии, а не ролевое решение — 403 тут
  не используется.
- Запрос без subject (например логин) BR32 не проверяется.
- Счётчик в памяти: перезапуск процесса обнуляет его, поэтому «8 часов неактивности»
  переживают перезапуск только через срок токена. Настоящее хранилище сессий — этап 4.
- Просроченный токен отдельно даёт обычный 401 от валидации JWT; BR32 — это
  отдельная проверка активности, обе приводят к 401, но `code` различается
  (`SESSION_EXPIRED` против пустого тела).


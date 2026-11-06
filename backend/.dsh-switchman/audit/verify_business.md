# Независимая перепроверка находок L4 (бизнес-логика) и L7 (тестирование)

Режим: только чтение исходников; `dotnet build/test` не запускались (testhost/Kestrel в песочнице
не запускаются) — все утверждения получены статическим чтением и grep. Источники:
`src/WorkTime.Application/Services/*.cs`, `src/WorkTime.Domain/*`, `src/WorkTime.Api/**`,
`src/WorkTime.Infrastructure/*`, `tests/WorkTime.Tests/*`, `docs/business_rules.md`,
`api-contracts.md`, `roadmap.md`.

Вердикты: **CONFIRMED** — подтверждено по строкам; **PARTLY** — верно, но формулировка неполная/
неточная (уточнение в колонке); **REJECTED** — неверно или преувеличено.

## 1. L4: соответствие BR (утверждения лана)

| Утверждение L4 | Verdict | Что проверено (file:line) и уточнение |
|---|---|---|
| БП1 «реализован неверно» | CONFIRMED | `DevRepositories.WorkTime.cs:151,156` — фильтр только `Status == Active` + равенство даты → после `finish` тот же день открывается повторно; `WorkTimeService.cs:62-68` (BR1), `:82` (запись без lock); `ReportService.cs:276-283` — часы одной даты складываются. |
| БП1 «блокирует следующий день» (в строке БП3) | REJECTED | `DevRepositories.WorkTime.cs:156` сравнивает `workDay.Date.Date == needle`: открытая смена на дату D **не** мешает старту на дату D+1. Корректно: открытая смена остаётся `Active` и не попадает в месячный отчёт (`DevRepositories.WorkTime.cs:61-64`) — это и есть нарушение БП3/БП15. |
| БП2 | CONFIRMED | `WorkTimeService.cs:104-109` (статус + `EndTime != null` → 409 BR2); двойной finish → тот же путь. |
| БП3 «отсутствует» | PARTLY | Триггера нет: grep `IHostedService|BackgroundService|Timer` по `src` → ноль (только комментарии `WorkTimeService.cs:3,130`, `WorkDaysController.cs:113`). Есть ручная ручка `WorkDaysController.cs:114-127` и запись «Auto-finish» `WorkTimeService.cs:166-178`. Уточнение L4 про «БП1 блокирует следующий день» — неверно (см. выше). |
| БП4 | CONFIRMED | Формула `WorkTimeService.cs:218-239`, `ReportService.cs:268-272`; проверки `endTime >= startTime` нет ни в запросе (`Requests.cs:47-51` — только regex формата), ни в сервисе → отрицательное «отработанное время» принимается. |
| БП5 | CONFIRMED | `WorkTimeService.cs:123`, `:161`; исключение из месячного отчёта `ReportService.cs:256-260`. |
| БП6 | CONFIRMED | `BreakService.cs:45-51` (старт только в открытом дне), `:110-116` (finish перерыва в закрытом дне → 409 BR6). |
| БП7 | PARTLY | Авто-закрытие открытых перерывов есть: `WorkTimeService.cs:111-112`, `:185-213`. Уточнение: `FinishOpenBreaksAsync` вызывается **вне** `lock (EntityMutationGates.WorkTime)` (`:112`, `:149`), хотя комментарий на `:192` утверждает «вызывается внутри lock»; lock реентрален, но между вызовом и входом в lock возможна интерлиация с параллельным finish/перерывом. |
| БП8 | PARTLY | `BreakService.cs:118-124` (409 BR8 при end<start), `:144` (`CountedInReports=false`). Но перерыв <5 мин **остаётся в отчёте**: `ReportService.cs:170-178` добавляет в `daily.Breaks` все перерывы дня, не только учитываемые → против БП8 «не сохраняются в отчётах». L4 это не отметил. |
| БП9 | PARTLY | `BreakService.cs:66` — флаг при старте, если сумма уже `>= 4h`; `:133` — при finish `otherBreaks + duration > 4h`. Уведомления руководителю нет (grep `notify|Notification` по `src` → ноль); при авто-закрытии флаг не пересчитывается (`WorkTimeService.cs:185-213`). Уточнение: на старте `>=`, на finish `>` — ровно 4h на старте помечается как «превышение», хотя БП9 говорит о превышении. |
| БП10 | CONFIRMED | `BreakService.cs:53-60` (поиск открытых по сотруднику), `:97-102` (повторный finish → BR10). |
| БП11 | CONFIRMED | Вычитание: `WorkTimeService.cs:226-238`, `ReportService.cs:268-272`, `:316-333`. |
| БП12 | CONFIRMED | `ReportService.cs:83-91` (отчёт только после завершения, BR14), `:106-113` (после закрытия дня — только Admin). Ветка «сотрудник правит свой отчёт до конца дня» недостижима. |
| БП13 | CONFIRMED | `ReportsController.cs:110-111`, `:150-151` — оба под `ManagerOrAdmin`; сотрудник комментарии не читает. |
| БП14 | PARTLY | `ReportService.cs:83-91` (409 BR14), `:152-155` (незавершённый день → `null` 200). Но «формируется **автоматически** после завершения дня» не реализовано: `FinishWorkDayAsync` (`WorkTimeService.cs:86-128`) отчёт не создаёт — нужен `POST /api/reports/{workDayId}` (`api-contracts.md:77`). |
| БП15 | CONFIRMED | `ReportService.cs:213-214`; `DevRepositories.WorkTime.cs:61-64` (только завершённые). |
| БП16 | PARTLY | Строки «ФИО × дни + итог»: `ReportService.cs:226-286`. Строки берутся из `GetActiveEmployeesInDepartmentAsync` (`:220`) → деактивированный сотрудник исчезает из месячной таблицы (против БП24). |
| БП17 | PARTLY | RBAC есть: `ReportsController.cs:161-162` (CSV) и `:184-185` (xlsx) под `ManagerOrAdmin`; Excel возвращает 501 (`:189-200`). Правило «экспорт в Excel доступен» не выполнено (этап 4). |
| БП18 | PARTLY | Произвольный период не поддержан: окно из месяца закрытия (`ReportService.cs:210-211`, `:296-312`). Формулировка L4 о расхождении с контрактом неточна: контракт противоречив сам — `api-contracts.md:83` («query как у эндпоинта 9») против `api-contracts.md:489` («нет параметров»); код следует §10 (`ReportsController.cs:184`). CSV-экспорт с query параметрами документирован (`api-contracts.md:488`) и реализован (`ReportsController.cs:164-180`). |
| БП19 | CONFIRMED | `Enums.cs:4-9`; месячный отчёт — `ManagerOrAdmin` (`ReportsController.cs:76`); Default/Fallback требуют аутентификации. |
| БП20 | CONFIRMED | `UsersController.cs:151-161` (PUT), `:199-203` (DELETE), `:226-230` (deactivate), `:284-309` (`TryGuardLastActiveAdmin`) — подсчёт и запись под `UserMutationLock.SyncRoot`. |
| БП21 | PARTLY | Есть: finish дня (`WorkTimeService.cs:98-101`), правка/чтение отчёта (`ReportService.cs:75-79`, `:145-150`), can-start (`WorkDaysController.cs:141-144`). Нет: auto-finish (`WorkTimeService.cs:131-147`, `WorkDaysController.cs:118-126`) **и вообще** `POST /api/breaks/{breakId}/finish` (`BreaksController.cs:58-62`, `BreakService.cs:86-149` — метод не принимает `employeeId`). |
| БП22 | PARTLY | Руководитель правит чужой текст отчёта до конца дня (`ReportService.cs:76`) **и** может завершить чужой перерыв (`BreaksController.cs:58`) — БП22 запрещает ему правку часов и перерывов. L4 упомянул только текстовую ветку. |
| БП23 | CONFIRMED (частично) | Правка текста админом (`ReportService.cs:107`); правки времени/перерывов нет ни на одном эндпоинте (`WorkDaysController.cs:43-130`, `BreaksController.cs:33,53`); логирование изменений — `AuditTrail` вызывается только в `UsersController.cs:112,180,208,235,267`, `DepartmentsController.cs:92,132,170`, `AdminController.cs:88,127` и `WorkTimeService.cs:178`. |
| БП23.1 | CONFIRMED (частично) | Сущность и close/reopen: `AdminController.cs:78-90`, `:117-129`. `IsClosed` нигде не блокирует редактирование (grep `IsClosed` → `Entities.cs:106`, `DevRepositories.Support.cs:272`, `AdminController.cs:78/83/117/122`, `ManagementWires.cs:137`). |
| БП23.2 | PARTLY | Причина обязательна (`AdminController.cs:109`, `RequestParsers.TryRequireText`), гриф «Экстренная корректировка» пишется в лог (`:127-129`), но «внести изменения» физически невозможно (нет эндпоинтов правки времени/перерывов). |
| БП24 | CONFIRMED | `AuthService.cs:45-53` (неактивный вход拒绝, `IsLocked=true`); `UsersController.cs:232` (данные сохранены); неактивный остаётся в списке (`IntegrationApiEndpointsTests.cs:878`). |
| БП25 | PARTLY | Ролевой гейт `AbsenceService.cs:41-47`, автор `:67`. Уточнение: «прогул» в модели — абстрактный `AbsenceReason.Absence` (`Enums.cs:18-24`), подсказка BR27 печатает «Absence» (`WorkTimeService.cs:242-255`), т.е. 4 причины правил реализованы с потерю имен. |
| БП26 | CONFIRMED | `ReportService.cs:233-247` (колонка отсутствий), `:262-266` (день с отсутствием не даёт часов). |
| БП27 | CONFIRMED | `AbsenceService.cs:89-135` (can-start по дате), `WorkTimeService.cs:46-56` (409 BR27 с причиной), tooltip `:242-255` + `Wire.cs:126` (`createdByUserId`). |
| БП28 | CONFIRMED | `AbsenceService.cs:41-47` (сотрудник не меняет), `:55-71` (переиспользование id под `EntityMutationGates.Absences`). |
| БП29 | CONFIRMED (частично) | Лог пишется только: auto-finish (`WorkTimeService.cs:166-178`), Users CRUD (`UsersController.cs:112,180,208,235,267`), Departments CRUD (`:92,132,170`), close/reopen (`AdminController.cs:88,127`). Не логируются start/finish дня, перерывы, правка отчёта, проставление отсутствия, комментарии. Чтение — `AdminController.cs:41-56` под `AdminOnly`; хранение 1 год отсутствует (`DevRepositories.Support.cs:160-176` — фильтр по `Timestamp` нет). |
| БП30 | CONFIRMED (частично) | `OldValue/NewValue` (`Entities.cs`) заполняются только в перечисленных записях; истории правок часов/перерывов/отчётов нет. |
| БП31 | CONFIRMED (отсутствует) | Ни один request не содержит подтверждения/токена идемпотентности (`Requests.cs:27-85`, `Requests.Management.cs`). |
| БП32 | CONFIRMED | `Middleware.cs:161-162` (лимит = `JwtSettings.ExpiryMinutes`, `appsettings.json:6` = 480 мин), `:236-256` (401 + `WWW-Authenticate: Bearer` + `code=SESSION_EXPIRED`), `:270` (учёт активности); трекер внедрён в логин (`AuthController.cs:24,31`). In-memory на процесс — контракт §14 честно говорит. |
| БП33 | CONFIRMED (отсутствует) | grep `backup` → только комментарий `InfrastructureLayer.cs:10`; планировщика/03:00/30 дней нет. |
| БП34 | CONFIRMED | Argon2id: `PasswordHasher.cs:103-111`, формат `:32` (`$argon2id$v=19$m=19456,t=2,p=1`); в модели только `PasswordHash` (`Entities.cs:11`). |
| БП35 | PARTLY | `AuthService.cs:107` — `lockRulesApply = Admin || Manager` → сотрудник никогда не блокируется (БП35 без ролевого ограничения). Пороги: 5 → 15 мин (`:119-126`, `WrongPasswordsToLock=5` `:22`, `LockDuration` `:24`), 10 → до конца дня (`:109-118`, `WorkTimeRules.cs:12`); досрочная разблокировка — `UsersController.cs:249-250` (`AdminOnly`). НЕ упомянуто: счётчик сбрасывается при успешном входе (`AuthService.cs:78-80`) и нигде не обнуляется по календарному дню → «10 неудачных попыток **за сутки»** не выполняется (это 10 подряд). |
| БП36 | CONFIRMED (частично) | `AuthService.cs:127-132` (`RequiresCaptcha`), `Wire.cs:22,240`; `LoginRequest` (`Requests.cs:27-33`) поля капчи не имеет, сервер капчу не проверяет → ограничение обходится повтором без капчи. |

## 2. L4 §4.2: расхождения `api-contracts.md` ↔ контроллеры

| Утверждение | Verdict | Evidence |
|---|---|---|
| Пути/методы/политики совпадают с §10 | CONFIRMED | `AuthController.cs:12-14` (анонимный login), `WorkDaysController.cs:13-16`, `BreaksController.cs:11-14` (`api` + `breaks/{id}/finish` `:53`), `ReportsController.cs:14-16`, `AbsencesController.cs:12-17`, `UsersController.cs:11-13`, `DepartmentsController.cs:9-11`, `AdminController.cs:14-17` + абсолютные `/api/audit-logs` `:41`, `/api/period-closures/.../close|reopen` `:61,97`. |
| `export.xlsx` → 501, но «§2 обещает query» | PARTLY | 501 есть: `ReportsController.cs:184-200`. Контракт внутренне противоречив: `api-contracts.md:83` против `api-contracts.md:489`. Код соответствует §10, поэтому «расхождение контроллер↔контракт» — неверная формулировка; реальная дыра — БП18 (произвольный период). |
| CSV-экспорт соответствует контракту | CONFIRMED (L4 не отмечал соответствия) | `ReportsController.cs:161-180` ↔ `api-contracts.md:488`; тело: BOM + `;` (`ManagementWires.cs:167-168`), колонка отсутствий (`:182`). |
| auto-finish без BR21 в контракте и в коде | CONFIRMED | `api-contracts.md:74`, `:475`; `WorkTimeService.cs:131-147`. |
| ProblemDetails §12 воспроизводится | CONFIRMED | `Middleware.cs:70-98` (+`WWW-Authenticate` `:98`), 404 с `entityType/entityId`, `ENTITY_ID_MISSING` `AdminController.cs:48-52` ↔ `api-contracts.md:503`. |
| Валидация: нет min/max длины | CONFIRMED | `Requests.cs:29-84` — `[Required]` + regex дат/времени, `MinLength/MaxLength` нет; `reason` парсится enum'ом (`Requests.cs:155-157`, код `REASON_INVALID`). |
| Casing wire camelCase, имена совпадают с §5 | CONFIRMED | `Wire.cs:94` (`totalMinutes`) ↔ `api-contracts.md:174`; `Wire.cs:113` (`absenceDays`) ↔ `:185`; `Wire.cs:126` (`createdByUserId`) ↔ `:195`; `ManagementWires.cs:49-51` (`closedBy`, `reopenReason`, `reopenedByUserId`) ↔ `:231-233`. |

## 3. L4 §4.3–4.4: граничные случаи и конкурентность

| Утверждение | Verdict | Evidence / уточнение |
|---|---|---|
| finish без start → 409 BR2; двойной finish → 409 | CONFIRMED | `WorkTimeService.cs:104-109`. |
| start после finish в тот же день → новый WorkDay, часы складываются | CONFIRMED | `DevRepositories.WorkTime.cs:167` (вставка по новому `Id`), `ReportService.cs:276-283`. |
| `endTime < startTime` → 200, отрицательные минуты | CONFIRMED | `ReportService.cs:166`, `:270`; `CountedInReports=false` при <15 мин (`WorkTimeService.cs:123`). |
| Пересечение полуночи: payload finish без даты | CONFIRMED | `Requests.cs:47-51`. |
| Границы дат inclusive, конец месяца корректный | CONFIRMED | `DevRepositories.WorkTime.cs:178-182`, `DevRepositories.Support.cs:123-127`, `ReportService.cs:343-347`. |
| PUT отдела допускает двухшаговую петлю родителей | CONFIRMED | `DepartmentsController.cs:120-123` — guard только `SELF_PARENT`. |
| «Репозитории (`Dictionary`) без синхронизации → `foreach` + `SaveAsync` = `InvalidOperationException` → 500» для WorkDay/Break | REJECTED | `DevRepositories.WorkTime.cs:16` и `:189` — `ConcurrentDictionary`; перечисление безопасного-итератора не бросает исключений. Реальный риск — «слабое» перечисление (может не увидеть только что добавленный день → guard BR1 пропускает гонку). Исключение возможно в `Dictionary`-репозиториях: `DevRepositories.Users.cs:17,130`, `DevRepositories.Reports.cs:11,69`, `DevRepositories.Support.cs:11,134,194`. |
| Два `POST /api/reports/{workDayId}` → два Report | CONFIRMED | `ReportService.cs:81-119` (read-modify-write без lock) + `DevRepositories.Reports.cs:11`. |
| SetAbsence и BR20 под lock | CONFIRMED | `AbsenceService.cs:56`; `UsersController.cs:157,201,228`. |
| sync-over-async внутри lock | CONFIRMED | `WorkTimeService.cs:120,125,163,210`; `BreakService.cs:132,136,146,169`; `AbsenceService.cs:60,69`. |
| Отсутствие отчёта → `null` 200; дублирование на уровне сервиса нет | CONFIRMED | `ReportService.cs:152-155`, `:81-116`. |
| Открытый/некорректный Id → 404 `ENTITY_NOT_FOUND` | CONFIRMED | `EntityNotFoundException` + `Middleware` → 404 с `entityType/entityId`; `AdminController.cs:75,114`. |

## 4. L7: пирамида, хост, покрытие

| Утверждение | Verdict | Evidence |
|---|---|---|
| Unit = 25, integration = 26, итого 51 | CONFIRMED | Anchored grep `^\s*\[Fact\]`: `WorkTimeServiceTests.cs` 9 (:22,69,123,164,207,263,308,370,428), `BreakServiceTests.cs` 4, `ReportServiceTests.cs` 8, `AuthServiceTests.cs` 4, `AbsenceServiceTests.cs` 4; `IntegrationApiEndpointsTests.cs` 26 (:326…:1227). (Счёт PowerShell'ом даёт 26 unit из-за совпадения `[Fact]` в комментарии `WorkTimeServiceTests.cs:11`.) |
| roadmap заявляет 55 (25+30) — завышено | CONFIRMED | `roadmap.md:5`, `roadmap.md:28`. |
| Новый `WebApplicationFactory` на тест; `ConfigureTestServices` меняет только `ILoggerFactory` | CONFIRMED | `IntegrationApiEndpointsTests.cs:176`. |
| Нет `UseEnvironment` и переопределения `JwtSettings` | CONFIRMED | `:176` — единственный изменённый сервис; токены подписываются dev-ключом из `src/WorkTime.Api/appsettings.json`. |
| Манипуляция временем через `FactoryServices` (BR32) | CONFIRMED | `:186`, `:256`, `:1218`; `IClock` нет. |
| Флаккость по часам (`DateTime.UtcNow`) | CONFIRMED | `Today => DateTime.UtcNow` `:166`; `SeedPeriodClosureAsync(DateTime.UtcNow.Year, …Month)` `:842`, `:1105`. |
| Argon2id `m=19456 KiB, t=2` | CONFIRMED | `PasswordHasher.cs:12-13`. |
| `/api/departments/*` — ноль тестов | CONFIRMED | В интеграционном файле отделы только сидируются через репозиторий (`:204-207`), HTTP-вызовов нет. |
| Экспорт CSV/xlsx и `/api/audit-logs` не тестируются | CONFIRMED | grep `export|audit-logs` по `IntegrationApiEndpointsTests.cs` → ноль. |
| 501 нигде не ассертируется | CONFIRMED | Тот же grep; `ReportsController.cs:184-200` без теста. |
| 401/403 ассертируются только по статусу | CONFIRMED | `:435`, `:464`. |
| Нет concurrency-тестов (`Task.WhenAll`) | CONFIRMED | grep → ноль. |
| BR22 «не покрыт» | PARTLY | Частично покрыт: `ReportServiceTests.cs:121` (`CreateOrUpdateReport_ManagerAfterDayFinished…`) и `IntegrationApiEndpointsTests.cs:738` («only admin edits after day end»). Не покрыто ровно то, что запрещает БП22: правка руководителем **часов и перерывов**. |
| BR25 «проверены только Sick/Leave» | PARTLY | Покрыты Sick (`AbsenceServiceTests.cs:115,121`), Leave (`:174,178`), DayOff (`:60`, `:231`, `Integration:863`). Не покрыт только `Absence` («прогул»). |
| BR3 частично (unit есть, триггера нет) | CONFIRMED | `WorkTimeServiceTests.cs:370` (`AutoFinishWorkDay_…`); `IHostedService` не существует. |
| BR8/BR9/BR13/BR17/BR18/BR19/BR20/BR23/BR23.1/BR23.2/BR29/BR30/BR31/BR32/BR33/BR34/BR35/BR36 статусы | CONFIRMED | См. карту покрытия ниже. |
| «Тестов репозиториев нет» | CONFIRMED | glob `tests/**/*.cs` → ни одного `*RepositoryTests.cs`. |
| «Unit-тесты работают на тестовых `InMemory*Repository`, боевые — shadowed» | PARTLY | Это не shadowing в C#-смысле, а две независимые реализации одних интерфейсов в разных сборках: runtime — `Program.cs:53-61` (`WorkTime.Infrastructure`), unit-тесты — `tests/WorkTime.Tests/InMemory*.cs`. Практический вывод верен: бизнес-правила проверяются на тестовой реализации. |
| BR35/BR36 role-deviation закреплен ассертом | CONFIRMED | `AuthService.cs:107`, `AuthServiceTests.cs:109`, `api-contracts.md:262,392`. |
| BR32 покрыт в обе стороны | CONFIRMED | `:1183`, `:1228` (+ `:256` откат активности). |

## 5. Точная карта покрытия BR (по фактическим именам тестов)

Покрыто напрямую (unit): BR1, BR2 (`WorkTimeServiceTests.cs:23,70,124`), BR3 (`:371`), BR4/BR11 (`:208`),
BR5 (`:264`), BR7 (`:429`), BR8 (`BreakServiceTests.cs:14`), BR9 (`:60`, `:106`), BR10 (`:153`),
BR12 (`ReportServiceTests.cs:16,61,121`), BR15/BR16/BR5-в-отчёте (`:236`, `:363`), BR21 (`ReportServiceTests.cs:301`,
`WorkTimeService` owner-ветки), BR25/BR28 (`AbsenceServiceTests.cs:10,66,130,183`), BR26 (`ReportServiceTests.cs:419`),
BR27 (`AbsenceServiceTests.cs:183`), BR35/BR36 (`AuthServiceTests.cs:13,55,109,154`).

Покрыто интеграционно: BR1 (`:605`), BR6 (`:628`), BR10 (`:649`), BR9 (`:676`), BR1–BR11 сквозной
(`:541`), BR14 (`:718`, `:836`), BR12 (`:738`), BR21 (`:773`, `:1015`), BR13 (`:803`), BR15/BR16 (`:836`),
BR24 (`:408`, `:878`), BR20 (`:910`), BR35 unlock (`:940`), BR27/BR28 (`:968`), BR23.1/BR23.2 (`:1099`),
BR32 (`:1183`, `:1228`), BR36 флаг (`:362`), 400/404 ProblemDetails (`:1034`, `:1074`, `:1161`).

Непокрытые (итог): **BR17** (экспорт, RBAC на экспортных путях), **BR18** (произвольный период),
**BR22** (правка руководителем часов/перерывов), **BR23** (обязательное логирование изменений админом),
**BR23.1** (настройка закрытия администратором/авто-закрытие по дате), **BR29** («все действия»,
хранение 1 год, доступ только админу), **BR30** (история изменений), **BR31** (подтверждение критических
действий), **BR33** (бэкап 03:00, 30 дней), **BR7-гонка**/двойные старты (concurrency),
**BR35** «10 за сутки → блок до конца дня», manager-unlock негатив (403), 10-неудач путь,
**BR36** сама проверка капчи, **BR8** «не сохраняется в отчётах», **BR9** уведомление руководителю,
**BR13** чтение комментариев сотрудником (негатив), **BR25** причина «прогул», **BR19** позитивный
«admin = полный доступ», **BR24** «заблокирован до конца дня», департаменты (6 эндпоинтов), 501-класс,
токен-негативы (tampered/expired/wrong audience/issuer), `auto-finish` через HTTP.
Косвенно/частично: BR3 (unit без триггера), BR19, BR20 (без гонки и без DELETE-админа), BR32 (без
«фоновый учёт не прерывается»), BR34 (без явного ассерта), BR23.2 (без ассерта грифа и записи лога).

## 6. missed — проблемы, которые находки L4/L7 НЕ упомянули

| # | Приоритет | Что | Влияние | Как исправить | Evidence |
|---|---|---|---|---|---|
| 1 | Critical | `POST /api/breaks/{breakId}/finish` не проверяет ни владельца, ни роль | Любой аутентифицированный (в т.ч. другой сотрудник) закрывает чужой перерыв; руководитель правит перерывы вопреки БП22; BR21 полностью отсутствует на пути | Добавить `employeeId` из токена и owner-gate в `FinishBreakAsync` (+ BR21 в контракт) | `BreaksController.cs:58-62`; `BreakService.cs:86-149` (сигнатура без `employeeId`); контракт тоже молчит: `api-contracts.md:76`, `:477` |
| 2 | High | Этап 3 (EF Core + Pomelo MySQL) не реализован: ни ссылки в csproj, ни `DbContext`, DI остаётся на in-memory | Вся персистентность dev-Stub; «боевые» репозитории — тестовые словари; гонки и транзакции不可 проверяемы | Реализовать EF-репозитории + contract-тесты; переключить DI | `Program.cs:53-61`; Select-String `Pomelo|EntityFramework|MySql|DbContext` по `src/*/*.csproj` и `src/**/*.cs` → ноль; `roadmap.md:61-65` (□, не выполнено) |
| 3 | High | BR35 «10 неудачных попыток **за сутки**»: счётчик — бегущий итог и сбрасывается при успешном входе; нет сброса по календарному дню | Блок «до конца дня» достижим только 10 подряд; успешные входы между неудачами обнуляют счётчик | Сбрасывать `FailedLoginAttempts` по календарному дню (или хранить окно суток) | `AuthService.cs:78-80`, `:105`, `:109-118`; контракт сам помечает как reserved: `api-contracts.md:429` |
| 4 | High | `PeriodClosure` не создаётся через API (нет POST), нет авто-закрытия по дате | БП23.1 «настраивается администратором» недостижим; месячный отчёт без закрытого периода возвращает `[]` | Эндпоинт создания/настройки закрытия + планировщик (5-е число) | `AdminController.cs:61,97` (только close/reopen); `ReportService.cs:204-208`; создание только в сиде/тестах (`DevSeed.cs:30-48`, `IntegrationApiEndpointsTests.cs:842,1105`) |
| 5 | High | BR14: daily report не формируется автоматически при завершении дня | «Отчёт формируется автоматически» не выполняется; клиент обязан POST | Вызвать `CreateOrUpdateReportAsync` из `FinishWorkDayAsync`/`AutoFinishWorkDayAsync` | `WorkTimeService.cs:86-128`, `:131-180` — вызова ReportService нет |
| 6 | Medium | BR30 авторство в AuditLog при auto-finish = subject токена, а не владелец смены | При закрытии чужой смены лог фиксирует «кто» неверно (и автор != целевой сотрудник) | Писать `UserId` = actor, и отдельное поле владельца/целевой сущности | `WorkTimeService.cs:166-178` (`UserId = employeeId` из токена); owner не проверяется `:131-147` |
| 7 | Medium | `GET /api/work-days/{employeeId}/can-start` запрещён руководителю и админу | Против БП19/БП22 (руководитель должен видеть состояние отсутствий/кнопки подчинённых) | Разрешить `ManagerOrAdmin` для чужого `employeeId` | `WorkDaysController.cs:141-144`; контракт это же закрепляет: `api-contracts.md:481` |
| 8 | Medium | BR8: перерыв <5 мин остаётся в `daily.Breaks` | Отчёт содержит перерывы, которые по правилу «не сохраняются в отчётах» | Фильтровать `CountedInReports` при построении списка | `ReportService.cs:170-178` |
| 9 | Medium | BR10 гонка на старте перерыва: проверка открытых перерывов вне lock | Два открытых перерыва одного сотрудника (нарушение БП10) в параллельных запросах | Перенести `GetUnfinishedForEmployeeAsync` + запись под `EntityMutationGates.WorkTime` | `BreakService.cs:54-60` (чтение вне lock, запись `:82` вне lock) |
| 10 | Medium | `FinishOpenBreaksAsync` вызывается вне `lock WorkTime`, вопре собственному комментарию | Возможна интерлиация закрытия перерывов с параллельным finish дня / start перерыва | Вызов внутри того же lock либо единый read-modify-write | `WorkTimeService.cs:112`, `:149` против `:192-194` |
| 11 | Low | BR9 граница: `>=` при старте (`BreakService.cs:66`) против `>` при finish (`:133`) | Ровно 4h на старте помечается как превышение; поведение нестабильно на границе | Unified `>` (или unified `>=`) и один helper | `BreakService.cs:66`, `:133`; `WorkTimeRules.cs:10` |
| 12 | Low | «Прогул» represented as generic `Absence`; tooltip BR27 печатает «Absence» | Подсказка не соответствует тексту правила «[Причина отсутствия]» для прогула | Переименовать/добавить `Truancy` и локализовать текст | `Enums.cs:18-24`; `WorkTimeService.cs:242-255` |
| 13 | Low | Имя теста `PeriodClosure_CloseBlocksEditing_…` обещает блокировку правки, но ассертит только close/BR23.1/reopen-причину | Тест вводит в заблуждение: закрытие периода правку не блокирует | Переименовать тест либо добавить ассерт блокировки | `IntegrationApiEndpointsTests.cs:1099-1117` |
| 14 | Low | Нет ни одного теста на `auto-finish` через HTTP и на департаменты (6 эндпоинтов с политиками) | RBAC и BR3 на HTTP-уровне не проверены | Добавить интеграционные тесты | grep по `IntegrationApiEndpointsTests.cs`: `auto-finish` → ноль; `departments` только репозиторийный сид `:204-207` |

## 7. Итог

* Подавляющее большинство находок L4 подтверждается по строкам; ошибочны/неточны: «БП1 блокирует
  следующий день» (REJECTED), «foreach + SaveAsync → InvalidOperationException» для WorkDay/Break
  (REJECTED, там `ConcurrentDictionary`), формулировка расхождения export.xlsx с контрактом (PARTLY,
  противоречие внутри самого контракта), BR14 «автоматически» и BR8 «не сохраняются в отчётах»
  (PARTLY), BR9 граница `>=`/`>` (PARTLY).
* Находки L7 по пирамиде (25 unit / 26 integration / 51 итого) и по дырам покрытия подтверждены;
  неточны: BR22 «не покрыт» (частично покрыт) и BR25 «только Sick/Leave» (ещё DayOff).
* Самые существенные пропуски обоих ланов: отсутствие owner-gate на `breaks/{id}/finish`,
  нереализованный этап 3 (EF/Pomelo), семантика BR35 «за сутки», отсутствие создания
  `PeriodClosure`, отсутствие авто-формирования отчёта и искажение авторства в AuditLog при auto-finish.

HANDOFF
completed: перепроверка всех утверждений L4 и L7 + спец-проверки + карта покрытия + missed; отчёт в
`E:\AItest\progcentr\project\backend\.dsh-switchman\audit\verify_business.md`.
key findings: см. таблицы выше (file:line).
remaining: прогон тестов и сбор покрытия (coverlet) невозможны в песочнице; проверка EF-репозиториев
после этапа 3.
next steps: fix owner-gate на breaks finish; EF-репозитории + contract-тесты; daily-reset счётчика
BR35; эндпоинт создания PeriodClosure; авто-отчёт при finish; аудит правок.

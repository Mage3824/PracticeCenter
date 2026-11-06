# Независимая перепроверка cluster "security" (read-only)

Дата: 2026-11-04. Исходники не изменены. Все вердикты — по конкретным строкам; формулировки утверждений не принимались на веру.

## 1. jwt-dev-key-in-repo-accepted-in-production — CONFIRMED
- `src/WorkTime.Api/appsettings.json:3` — `SecretKey = "work-time-development-signing-key-change-me-please-32+-bytes"`; отдельного `appsettings.Production.json` в репозитории нет (glob по *.json), поэтому в Production ключ берётся оттуда же.
- `src/WorkTime.Api/JwtSettings.cs:10-11` — константа `DefaultDevelopmentSecretKey`; `TryFromConfiguration` подменяет dev-ключ только когда секция/ключ **отсутствуют или пусты** (:37-49, :52-64). Поскольку appsettings.json всегда даёт non-empty dev-строку, ни одна ветка не срабатывает.
- `ValidateSettings` (`JwtSettings.cs:74-96`) проверяет: Issuer/Audience непустые (:76-81), UTF8-длина ключа ≥ 32 (:83-88), `ExpiryMinutes > 0` (:90-95). Совпадение строки с dev-ключем не проверяется; `environment` в `ValidateSettings` не передаётся.
- Ключ HMAC = `SHA256(UTF8(secret))`: `TokenService.cs:33-34` (подпись) и `JwtBearerSetup.cs:77-78` (валидация) — идентичны, значит все среды подписывают/валидируют одним публично известным ключом.
- Валидация issuer/audience/lifetime включена (`TokenService.cs:82-86`), `ClockSkew = 1 мин` (:71, :89) — подделка требует знания секрета, но секрет в репозитории.
- Уточнение к формулировке: «проверяет только непустоту и длину» — непустота ключа проверяется в `TryFromConfiguration`, а `ValidateSettings` дополнительно сверяет Issuer/Audience и ExpiryMinutes. Суть (отказ на dev-ключ вне Development отсутствует) верна.

## 2. br32-session-resurrection-after-prune — CONFIRMED
- `Middleware.cs:168-176` — нет записи ⇒ `return false` (не просрочено).
- `Middleware.cs:185-204` — `TryPruneExpired` удаляет записи старше `_limit` (= `ExpiryMinutes`, :161-162) под `_pruneLock`; вызывается только из `TryRecordActivity` (:178-183) и throttled «раз в _limit» (:189-192) — т.е. prune запускается чужим аутентифицированным запросом или успешным входом (`Controllers/AuthController.cs:71`).
- `Middleware.cs:236-260` — 401 только если запись есть и просрочена; `finally` (:264-271) пишет новый baseline любым аутентифицированным запросом.
- Тред-безопасность трекера корректна: `ConcurrentDictionary` (:154) + `TryRemove` (:200) — порча бакетов тут не реальна; реальный дефект — семантика «нет записи = живо».

## 3. token-not-revocable-deactivated-or-demoted-user — CONFIRMED
- Роль и subject в claims: `TokenService.cs:37-42` (ни `jti`, ни `nbf`-специальных полей); `TryIssueAccessToken(user.Id, user.Role, ...)` берёт роль из БД на момент входа (`Controllers/AuthController.cs:73`).
- Политики сравнивают только роль токена: `AuthorizationPolicies.cs:44-50` (`RolesAuthorizationRequirement` поверх `ClaimTypes.Role`, :90-91 в `TokenService.cs`).
- Middleware проверяет лишь наличие subject: `Middleware.cs:226-232`; IsActive/роль из репозитория не сверяются нигде: `Identity.cs:59-72` (`TryRequireKnownUserAsync` возвращает User, но не смотрит в `IsActive`).
- Деактивация: `Controllers/UsersController.cs:213-234` (`user.IsActive = false` под `UserMutationLock`), unlock (:249-250) — выданный токен не инвалидируется; logout-эндпоинта нет (grep по контроллерам: анонимен только `POST /api/auth/login`, `AuthController.cs:40-41`).
- Дешёвый фикс точнее, чем в утверждении: контроллеры уже резолвят User (`Identity.cs:59-72`) — достаточно сверять `user.IsActive` (и роль) там же или в middleware.

## 4. no-rate-limiting-login-brute-force-and-kdf-dos — PARTLY
- Rate limiting нет: grep по `src` даёт ноль совпадений `AddRateLimiter|UseRateLimiter|EnableRateLimiting`; конвейер `Program.cs:180-195` (ExceptionHandler → RequestLogging → Swagger → UseAuthentication → BR32 → UseAuthorization → MapControllers) не содержит их.
- Единственный анонимный эндпоинт: `AuthController.cs:40-41` `[AllowAnonymous]`, `POST /api/auth/login`.
- Стоимость попытки: `PasswordHasher.cs:12-14` — `MemorySize = 19456` KiB (19 MiB), `Iterations = 2`, `Parallelism = 1`; вызов на каждую попытку с известным логином: `AuthService.cs:156` → `PasswordHasher.cs:89` (`HashPassword`) и :91 (`FixedTimeEquals`).
- НЕ точна часть «нет лимита тела запроса»: в приложении лимит не задан, но Kestrel по умолчанию принимает до **30 MB**, так что «неограниченный» неверно — тело ограничено 30 MB, что всё равно на порядки больше нужного для `{login,password}`. Фикс должен ставить ~64 KB (не 1 MB).
- Стоимость считается и при успешном входе (`AuthService.cs:73`), и только для известного логина — см. missed «enumeration/timing oracle».

## 5. br35-br36-skipped-for-employee-role — PARTLY
- Код подтверждён: гейт `lockRulesApply` — `AuthService.cs:107`, три ветки под ним: :109 (до конца дня), :119 (15 мин, `WrongPasswordsToLock = 5`, `LockDuration = 15 мин`, :22-24), :127 (капча, `WorkTimeRules.CaptchaThreshold`); `src/WorkTime.Domain/WorkTimeRules.cs:12,14` — `MaxFailedAttempts = 10`, `CaptchaThreshold = 2`.
- БП35/БП36 (`docs/business_rules.md:66-67`) роль не ограничивают — верно.
- Но утверждение «тесты сверяют только Manager/Admin — добавить Employee-кейс» неверно: `tests/WorkTime.Tests/AuthServiceTests.cs:109-150` уже содержит `Login_EmployeeIsNeverLockedOrAskedForCaptcha` и **закрепляет** текущее поведение (`Assert.False(fifthAttempt.IsLocked)` :142, `Assert.False(fifthAttempt.RequiresCaptcha)` :143).契约 тоже закрепляет: `api-contracts.md:262` («manager/admin only»), `api-contracts.md:392-394`. Значит фикс требует прав контракта **и** инверсии этого теста, а не добавления нового.

## 6. captcha-flag-not-verified-server-side — CONFIRMED
- `AuthService.cs:127-132` — только флаг; `Wire.cs:22` (`requiresCaptcha`), :240 (маппинг); `Requests.cs:27-34` — `LoginRequest` содержит только `login`/`password`, сервер капчу не проверяет (grep по `src`: captcha нигде не валидируется).
- Нюанс: `api-contracts.md:430` прямо помечает это как известный пропуск этапа («no challenge/response endpoint, no verification»), так что это не скрытая деградация, но БП36 действительно ничем не обеспечен.

## 7. manager-edits-other-employees-report-text — PARTLY
- Код подтверждён: класс-level `[Authorize]` (`ReportsController.cs:16`, action :91 без политики), гейт `ReportService.cs:76` пропускает Manager на чужом дне; логирования по БП23 в этом action нет (:96-106 — ни `AuditTrail`, ни role-ветки).
- attribution правил неверна: БП22 (`docs/business_rules.md:49`) запрещает руководителю править **рабочые часы и перерывы**, а не текстовый отчёт; право править текстовые отчёты любого сотрудника с обязательным логированием принадлежит администратору — БП23 (:50). Значит нарушение — БП23 (+ БП21 по смыслу), а не БП22.
- Фикс неточен: код исключения в сервисе — `BR21` (`ReportService.cs:78`), а не `BR12`; контракт `api-contracts.md:478` объявляет endpoint 8 как «аутентифицированный», поэтому `[Authorize(Policy = EmployeeOnly)]` меняет контракт (§7.3/§8), и Manager-путь должен остаться через явную политику + БП23-логирование.
- Чтение чужого отчёта сотрудником закрыто корректно: `ReportService.cs:145-150` (Employee + чужой день ⇒ 409 BR21).

## 8. swagger-ui-anonymous-in-production — CONFIRMED
- `Program.cs:183-184` — `app.UseSwagger()` / `app.UseSwaggerUI` без env-гейта, до `app.UseAuthentication()` (:186). Комментарий `AuthorizationPolicies.cs:11-13` прямо говорит, что Swagger остаётся анонимным; фильтр `BearerSecurityRequirementOperationFilter` (`Program.cs:162`, класс :210) помечает Bearer-требования по эндпоинтам.
- Реальный риск — публичный `/swagger/v1/swagger.json` (пути, схемы, ролевые требования) в Production; Swagger UI сам по себе не даёт доступ, но удобен для ручного перебора `/login` (см. утверждение 4).

## 9. monthly-report-n-plus-one-await-loop — CONFIRMED
- `ReportService.cs:214` — `GetFinishedDaysForPeriodAsync(periodStart, periodEnd, ct)`: дни на всю систему, без фильтра по отделу/сотруднику; :217 — отсутствия за период без фильтра; :220 — сотрудники **фильтруются по отделу** (`GetActiveEmployeesInDepartmentAsync`).
- Вложенные циклы: :224 (по сотрудникам) → :249 (по всем дням периода) → :263 `TryHasAbsenceOn` (линейный скан всех отсутствий, :350-356) → :271 `await TrySumCountedBreakTimeAsync(workDay.Id, ct)` на каждую ячейку (:316-323 — один репозиторный вызов + цикл по перерывам дня).
- Арифметика 200×31 ≈ 6200 репозиторных вызовов на один запрос корректна по структуре; `ReportsController.cs:161` и :184 (CSV/xlsx) идут тем же путём.
- Контракт на пачечный fetch перерывов действительно отсутствует: `src/WorkTime.Application/Interfaces/IRepositories.cs:86-88` есть только `GetAllByIdsAsync`/`GetUnfinishedForEmployeeAsync`.

## 10. sync-over-async-inside-global-locks — CONFIRMED
- 17 совпадений `GetAwaiter().GetResult()` (grep), все внутри `lock` (границы проверены): `WorkTimeService.cs:115-126`, :154-163, :194-212 (в т.ч. :210 в цикле под lock); `BreakService.cs:128-147` (:132, :136, :146); `AbsenceService.cs:56-86` (:60, :69, :84); `AuthService.cs:76-82`, :103-140 (:81, :139); `UsersController.cs:157-177`, :201-206, :228-234, :294 (внутри lock вызывающего action :228).
- Утяжеление: под глобальным замком блокируется поток пула во время чужой async-операции → starvation; в dev-tier репозитории все `Task` завершены синхронно, так что сегодня риск ограничен, но с EF станет реальным.

## 11. unguarded-dictionary-singleton-races — PARTLY
- Plain `Dictionary` в синглтонах подтверждён: `DevRepositories.Users.cs:17`, :130; `DevRepositories.Support.cs:11`, :134, :194; `DevRepositories.Reports.cs:11`, :69. Эталон `ConcurrentDictionary` — `DevRepositories.WorkTime.cs:16`, :189 и трекер `Middleware.cs:154`.
- Внешний гейт на структурные Add/Remove отсутствует: grep по `lock` даёт замки только в сервисах и `UsersController.cs:157/201/228`; `DepartmentsController.cs:91` — `SaveAsync` без lock (:87-94), `ReportsController.cs` и `AdminController.cs` без замков.
- Уточнение: часть путей всё же защищена — CRUD пользователей идёт под `UserMutationLock.SyncRoot` (`UsersController.cs:157/201/228`, тот же объект, что `EntityMutationGates.Users`, `EntityMutationGates.cs:16`), поэтому «запись идёт из контроллеров без lock» верно для departments/reports/audit/closures, но не для users.
- Формулировка о порче бакетов/бесконечном rehash-цикле соответствует известному поведению `Dictionary` при конкурентном Add/Remove во время enumerate.

## 12. in-memory-state-blocks-horizontal-scaling — PARTLY
- 9 репозиториев-синглтонов (`Program.cs:53-61`) и `UserActivityTracker` синглтон (:93-94) подтверждены; `IDistributedCache`/Redis/response compression не зарегистрированы (grep по src — ноль совпадений).
- Утверждение про сид неверно: `Program.cs:171-177` — сид выполняется **только** при `app.Environment.IsDevelopment() && IsManualRun()` (`:200-204` проверяет сборник входа), т.е. на боевом инстансе (Production, или тестовый хост) он не пишется.
- Остальное верно: BR32 считается per-process (`Middleware.cs:152-204`, `:266-270`), BR20 защищён process-wide мьютексом (`EntityMutationGates.cs:4-9`, `UsersController.cs:286-300`).

## 13. process-wide-mutation-mutex-serializes-throughput — CONFIRMED
- `EntityMutationGates.cs:4-9` — три `static readonly object` на процесс; `UserMutationLock.SyncRoot = EntityMutationGates.Users` (:14-17).
- Замки на горячих путях: `WorkTimeService.cs:115/154/194`, `BreakService.cs:128`, `AbsenceService.cs:56`, `AuthService.cs:76/103`, `UsersController.cs:157/201/228` (+ BR20-скан :294 под тем же замком).
- ВEF-эталоне гейт ничего не защищает на 2+ инстансах — верно (в памяти одного процесса). Связанное усиление: внутри этих замков синхронный блок async-операций (утверждение 10).

## 14. cancellation-token-never-honored — PARTLY
- Факт подтверждён: grep по `src` — ноль `ThrowIfCancellationRequested`, ни одна реализация не использует токен (`DevRepositories.Users.cs:19-23`, `DevRepositories.Support.cs:21-24`, `DevRepositories.Reports.cs:13-16`).
- «90 сигнатур в src/WorkTime.Infrastructure» преувеличено: фактическое число сигнатур с `CancellationToken cancellationToken` в Infrastructure — **57** (Select-String по `src/WorkTime.Infrastructure/*.cs`).
- Остальная часть (EF без токена не отменяет запрос в MySQL, тяжёлый месячный отчёт не прерывается) верна по структуре кода.

## 15. audit-log-unbounded-and-no-paging — CONFIRMED
- `DevRepositories.Support.cs:134` — `Dictionary<Guid, AuditLog>` без eviction; `GetHistoryForEntityAsync` (:160-172) сканирует весь словарь и возвращает всю историю сущности без страниц.
- `AdminController.cs:41-56` — `GET /api/audit-logs` отдаёт весь массив; `entityId` обязателен (:48-53), класс-level `AdminOnly` (:16) — доступ закрыт, объём нет.
- Записи добавляются на критические действия: `UsersController.cs:208-209`, `DepartmentsController.cs:92-94`, `ManagementWires.cs`, `WorkTimeService.cs` — подтверждены вызовы `AuditTrail.TryWriteAsync`.

## Дополнительные проблемы, НЕ упомянутые набором

### A. Enumerация учётных записей + timing oracle на /login (High)
- `AuthService.cs:31-42` — неизвестный логин даёт message «[RU] No account with this login.»; :136 — «[RU] Wrong password.»; :73 — KDF выполняется только для известного логина, поэтому ответ на неизвестный логин практически мгновенный. `AuthController.cs:55-57` — отказ возвращается как данные (200), без 401/429.
- Impact: анонимный дешёвый оракул «логин существует?» (по тексту и по времени ответа) до перебора паролей; усиливает утверждение 4.
- Fix: единый текст отказа («Неверный логин или пароль»), для неизвестного логина — фиктивная Argon2-верификация по эталонному хешу (выравнивание времени ответа), счётчик неудач на partition (IP+login), и `429` вместо «вечных 200».

### B. Нет TLS/HSTS/HTTPS-redirect; AllowedHosts — мёртвая настройка (High)
- Конвейер `Program.cs:180-195` не содержит `UseHttpsRedirection`/`UseHsts` (grep по src — ноль); `appsettings.json:21` — `"AllowedHosts": "*"` при отсутствии `app.UseHostFiltering()` (grep — ноль), т.е. настройка ничего не ограничивает.
- Impact: Bearer-токены (480 мин, не отзываемые — утверждение 3) идут по HTTP; перехват токена = полный угон сессии, включая Admin; нет защиты от downgrade.
- Fix: `app.UseHsts()` вне Development + `app.UseHttpsRedirection()`, TLS на Kestrel/reverse-proxy; `UseHostFiltering()` с реальным списком, либо убрать `AllowedHosts`.

### C. DevSeed с фиксированными паролями (High)
- `DevSeed.cs:22-28` — `admin/manager/employee` и `admin-password-1`, `manager-password-1`, `employee-password-1`; `NewUser` (:64-75) пишет их через `PasswordHasher.TryHash`. Сид включается при Development + ручном запуске (`Program.cs:171-177`, `IsManualRun` :200-204).
- Impact: боевой хост, запущенный с `ASPNETCORE_ENVIRONMENT=Development` (сценарий, который утверждение 1 уже называет), получает реальную учётную запись Admin с публично известным паролем — вход без подделки токена; вместе с dev-ключом открыты оба пути.
- Fix: dev-креденциалы генерировать случайно (`RandomNumberGenerator`) или из user-secrets и печатать в консоль; fail-fast при `Development` на боевом хосте; сид не выполняется в Production (уже) — но это не достаточно для Development-прода.

### D. Нет политики пароля и предельной длины (Medium)
- `Requests.cs:27-34` — только `[Required]`, без `[MaxLength]`/сложности; БП34 (`docs/business_rules.md:65`) требует только хеширования.
- Impact: неограниченная длина пароля увеличивает стоимость Argon2 на попытку (`PasswordHasher.cs:103` кодирует весь вход) и вместе с дефолтным лимитом Kestrel 30 MB превращает /login в CPU/RAM-усилитель; слабые пароли принимаются при создании пользователя.
- Fix: `[MaxLength(1024)]` на login/password, политика сложности при создании пользователя, явный `Limits.MaxRequestBodySize ≈ 64 KB` для auth-пути.

### E. Конверт валидации эхо-имена полей и тексты правил (Low)
- `Program.cs:102-135` — `reasons.Add(entry.Key + ": " + error.ErrorMessage)` (:115) и сборка Detail/message/error (:119-134) для model-state.
- Impact: анонимные пробы /login получают внутренние имена полей и правила валидации (подсказка для атакующего); контроллеры для критичных полей уже используют `FIELD_MISSING` (`AuthController.cs:48-49`), так что leak касается остальных полей.
- Fix: фиксированные русские сообщения по коду поля, причины — в лог, не в wire.

### F. Аутентификационные события не пишутся в audit log (Medium)
- `AuthService.cs:103-137` — блокировки/капча/неудачные попытки только меняют `User` и возвращают флаги; ни одного `AuditTrail` вызова. Единственный след — `LogWarning` в BR32 middleware (`Middleware.cs:238-239`).
- Impact: brute-force и разблокировки (BR35/БП35) невосстановимы для incident response; БП30-история покрывает только правки сущностей.
- Fix: писать события LOGIN_FAILED / ACCOUNT_LOCKED / UNLOCKED в `IAuditLogRepository` (или структурированные ILogger-события), с subject и actorId.

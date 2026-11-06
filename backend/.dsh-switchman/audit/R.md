# Лан R — Эталонные стандарты и актуальные рекомендации (веб-исследование)

Дата: 2026-11-04. Режим: read-only (исходники не менялись; создан только этот файл).
Все ссылки реально получены через web_search / web_fetch (raw-URL возвращали HTTP 200; 404 не цитируются).

## 1. jwt-hs256-key-length (Critical)
Эталон: RFC 7518 §3.2 (JWA) — ключ для HMAC-SHA256 должен быть не короче 256 бит (32 байта); OWASP JWT Cheat Sheet
перечисляет HS256/HS384/HS512 как «Recommended», требует CSPRNG-генерацию секрета, uniqueness и запрет переиспользования
одного ключа для разных приложений/сред, запрет хранения секрета в репозитории.
Для проекта: JWT HS256 с TTL 8 часов подписывается секретом из appsettings.json (JwtSettings.cs:97, TokenService.cs:94).
Короткий/коммитированный секрет → offline brute force подписи → подделка claim role=Admin → обход политик
AdminOnly/ManagerOrAdmin (AuthorizationPolicies.cs:52) и всех admin-эндпоинтов (AdminController.cs:133, UsersController.cs:317).
Применение:
```csharp
// Program.cs / JwtSettings.cs
if (jwt.Key.Length < 32) throw new InvalidOperationException("JWT signing key must be >= 256 bits");
// секрет только из env/secret store; генерация: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
```
Refs:
- https://github.com/OWASP/CheatSheetSeries/blob/master/cheatsheets/JSON_Web_Token_Cheat_Sheet.md
- https://raw.githubusercontent.com/OWASP/CheatSheetSeries/master/cheatsheets/JSON_Web_Token_Cheat_Sheet.md
- https://www.rfc-editor.org/rfc/rfc7518#section-3.2
- https://security.stackexchange.com/questions/95972/what-are-requirements-for-hmac-secret-key

## 2. jwt-revocation (Critical)
Эталон: OWASP JWT Cheat Sheet прямо: «if you use JWTs for user sessions, you will need a solution for managing session
invalidation … achieved using a deny list of revoked sessions/tokens»; альтернатива — Token Status Lists
(draft-ietf-oauth-status-list) с claim `status {status_list:{idx,uri}}`; replay protection через JWT denylist.
JWT сам по себе не отзываемый: 8 часов после logout, деактивации пользователя или смены роли токен остаётся валидным.
Применение (Этап 3/4): revocation-таблица или «revokedAt» на User + проверка в OnTokenValidated:
```csharp
options.Events = new JwtBearerEvents {
  OnTokenValidated = ctx => userRevoked(ctxPrincipal.Claims["sub"], ctxPrincipal.Claims["jti"])
                            ? Task.Fail() : Task.CompletedTask };
```
Refs: https://github.com/OWASP/CheatSheetSeries/blob/master/cheatsheets/JSON_Web_Token_Cheat_Sheet.md,
https://raw.githubusercontent.com/OWASP/CheatSheetSeries/master/cheatsheets/JSON_Web_Token_Cheat_Sheet.md

## 3. access-token-lifetime (High)
Эталон: OWASP API Security — «short-lived access tokens» для user-facing API; ASVS V3 (session management): idle timeout
30 min для L2/высокорисковых, до 12 ч/30 дней как верхняя граница с ре-аутентификацией; JWT Cheat Sheet: срок жизни
минимальный, long-lived токены опасны без revocation.
Для проекта: 8 ч access-токена без refresh и без отзыва — верхняя граница эталона, а не эталонное значение.
Применение: access 10–15 мин + refresh-токен (rotation + revoke в той же revocation-таблице); либо оставить 8 ч
только после внедрения п. 2.
Refs: https://owasp.org/www-project-api-security/, https://github.com/OWASP/CheatSheetSeries/blob/master/cheatsheets/JSON_Web_Token_Cheat_Sheet.md,
https://github.com/OWASP/ASVS/blob/master/4.0/en/0x12-V3-Session-management.md

## 4. iss-aud-alg-clockskew (High)
Эталон: OWASP API guidance: «Check the signature with an allowlisted algorithm, reject unsigned tokens, and verify the
issuer, audience and expiry». IdentityModel `TokenValidationParameters`: DefaultClockSkew = 300 секунд (5 мин) по умолчанию.
Для проекта: в JwtBearer (Program.cs:242, Identity.cs:73) без яных ValidIssuer/ValidAudience и ClockSkew токены другого
провайдера/среды принимаются, а 5-минутный skew плюс 8 ч TTL увеличивает окно жизни украденного токена.
Применение:
```csharp
options.TokenValidationParameters = new TokenValidationParameters {
  ValidateIssuer = true, ValidIssuer = "worktime-api", ValidateAudience = true, ValidAudience = "worktime-clients",
  ValidateLifetime = true, ValidateIssuerSigningKey = true, ClockSkew = TimeSpan.FromSeconds(30) };
```
Refs: https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/TokenValidationParameters.cs,
https://github.com/OWASP/CheatSheetSeries/blob/master/cheatsheets/JSON_Web_Token_Cheat_Sheet.md, https://owasp.org/www-project-api-security/

## 5. argon2id-parameters (High)
Эталон (OWASP Password Storage Cheat Sheet, дословно): «Use Argon2id with a minimum configuration of 19 MiB of memory, an
iteration count of 2, and 1 degree of parallelism»; соль уникальна для каждого пароля и хранится вместе с хешем;
рекомендуемая длина соли 16 байт (128 бит), тег 32 байта; bcrypt fallback work factor ≥10; PBKDF2 fallback 600k итераций (FIPS).
Для проекта: PasswordHasher.cs:124 на Konscious.Argon2 1.3.1 — параметры дефолтные (64 MiB / t=3 / p=1) или произвольные;
сравнение хеша должно быть constant-time.
Применение:
```csharp
var argon = new Argon2id(password) { SaltSize = 16, DegreeOfParallelism = 1, MemorySize = 19 * 1024, Iterations = 2 };
byte[] hash = argon.GetBytes(32);
bool ok = CryptographicOperations.FixedTimeEquals(storedHash, recomputed);
```
Refs: https://raw.githubusercontent.com/OWASP/CheatSheetSeries/master/cheatsheets/Password_Storage_Cheat_Sheet.md,
https://github.com/Vonage/gosrvlib/blob/v1.108.4/pkg/passwordhash/options.go

## 6. password-migration-pepper-length (Medium)
Эталон: OWASP Password Storage — параметры KDF хранятся вместе с хешем (сам описывает формат/параметры), pepper как defense-in-depth,
миграция старых хешей «on login» при первом успешном входе, без даунтайма; не ограничивать/не обрезать пароль слишком коротким лимитом.
Для проекта: в Users (DevRepositories.Users.cs:189) нужен сам-описывающий формат (алгоритм, m, t, p, соль, тег) + колонка версии,
и разумный лимит длины входа, чтобы Argon2 с 19 MiB не стал CPU/DoS-вектором на login (связано с п. 7).
Применение: `PasswordHash = "argon2id$v=19$m=19456,t=2,p=1$<b64salt>$<b64tag>"`; при верификации старого формата — пересчёт и запись нового.
Refs: https://raw.githubusercontent.com/OWASP/CheatSheetSeries/master/cheatsheets/Password_Storage_Cheat_Sheet.md

## 7. rate-limiting-middleware (High)
Эталон (ASP.NET Core 8, «Rate limiting middleware in ASP.NET Core»): `Microsoft.AspNetCore.RateLimiting`; политики
глобальные и именованные; `PartitionedRateLimiter.Create<HttpContext,string>` по identity/IP; лимитеры FixedWindow, SlidingWindow,
TokenBucketBox, Concurrency; `AddRateLimiter`, `RequireRateLimiting`, rejection → 429 (OnRejected), загрузка обязательно тестируется.
Для проекта: в Program.cs:242 нет AddRateLimiter → /api/auth/login без защиты (brute force BR35/BR36), тяжёлые отчёты
(ReportsController.cs:202, ReportService.cs:365) и Argon2-verify легко перегружают сервер.
Применение:
```csharp
builder.Services.AddRateLimiter(o => {
  o.RejectionHandling = ctx => Results.StatusCode(429);
  o.AddPolicy("login", p => System.Threading.RateLimiting.FixedWindowRateLimiter
      new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0 }));
  o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext,string>(c => RateLimitPartition.GetFixedWindowLimiter(
      c.Connection.RemoteIpAddress?.ToString() ?? "anon", _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(1) }));
});
app.UseRateLimiter(); // до UseAuthorization для /login
[Authorize][EnableRateLimiting("login")] // в AuthController.cs:78
```
Refs: https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/performance/rate-limit.md

## 8. lockout-captcha-thresholds (Medium)
Эталон (OWASP WSTG WSTG-ATHN-03 «Weak Lock Out Mechanism»): типичный порог блокировки — 5–10 неуспешных попыток
(3–5 упоминается как частый), разблокировка — по фиксированному периоду, self-service или администратором;
механизм должен балансировать защиту от brute force и denial-of-service легитимного пользователя; CAPTCHA может быть достаточной мерой.
Для проекта: BR35 (5 → блок 15 мин, 10 → блок до конца дня) и BR36 (капча после 2 неудач) попадают в эталонный диапазон,
но нужны: серверный счётчик (не клиентский), ключ по аккаунту И по IP/source, отсутствие утечки «этот аккаунт заблокирован»,
и rate limit на login (п. 7) — иначе блокировка чужого аккаунта = DoS.
Refs: https://owasp.github.io/www-project-web-security-testing-guide/latest/4-Web_Application_Security_Testing/04-Authentication/03-Weak_Lock_Out_Mechanism

## 9. problem-details-dev-prod (High)
Эталон (ASP.NET Core «Handle errors»): Developer Exception Page только в Development; `UseExceptionHandler` для production
(ловит и логирует необработанные исключения, перезапускает запрос на alternate pipeline); детали исключения (stack trace)
не отдаются наружу в production; ProblemDetails по RFC 7807.
Для проекта: Middleware.cs:298, ApiExceptions.cs:25, Program.cs:242 — важно, чтобы `ProblemDetailsOptions.IncludeErrorDetails`
был включён только в Development и чтобы внутренние сообщения WorkTimeException не текли в `detail` в prod.
Применение:
```csharp
builder.Services.Configure<ProblemDetailsOptions>(o => o.IncludeErrorDetails = builder.Environment.IsDevelopment());
```
Refs: https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/fundamentals/error-handling.md

## 10. cors-and-object-or-function-level-authorization (High)
Эталон (ASP.NET Core CORS docs, дословно): CORS «Is **not** a security feature, CORS relaxes security» — relaxed same-origin;
нужны явные origin'ы, не «allow any». OWASP API Security Top 10 (2023, официально добавлены/объединены: Broken Object Property Level
Authorization = слияние Excessive Data Exposure + Mass Assignment, Unrestricted Access to Sensitive Business Flows, SSRF,
Unsafe Consumption of APIs; плюс Broken Access Control, Unrestricted Resource Consumption, Broken Function Level Authorization):
авторизация проверяется на сервере по каждому объекту и по функции (admin-only функции отдельно).
Для проекта: в Api нет явного CORS-policy (Program.cs:242) — не использовать WithOrigins("*") с credentialed запросами;
проверять принадлежность объекта (WorkDays/Breaks/Absences по userId, Departments по manager scope) и роли на уровне функции,
а не только `[Authorize]` по политике (Wire.cs:470, ManagementWires.cs:227, Controllers/*).
Применение: `builder.Services.AddCors(o => o.AddPolicy("spa", b => b.WithOrigins(cfg.SpaOrigin).AllowAnyHeader().AllowAnyMethod()));`
Refs: https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/security/cors.md, https://owasp.org/www-project-api-security/,
https://raw.githubusercontent.com/OWASP/API-Security/master/README.md

## 11. ef-mysql-datetime-timezone-serverversion (High)
Эталон: Pomelo (commit 6b1e9ca) — `DateTimeOffset` хранится как UTC datetime в БД, а при трансляции сравнивается с
`@@session.time_zone`; wiki «Configuration Options»: обязательный выбор версии сервера (`MySqlServerVersion`/`MariaDbServerVersion`
или `ServerVersion.AutoDetect()`/`ServerVersion.Parse()`), `EnableRetryOnFailure`, `DefaultDataTypeMappings`
(в т.ч. `WithClrBoolean`).
Для проекта (Этап 3): time-чувствительные BR (BR3 AutoFinish в 23:59:59, BR33 бэкап 03:00, BR35 «до конца дня», Europe/Moscow в UI)
ломается, если `DateTime.Now`/lokal'ные DateTime попадают в DATETIME без явного UTC или с сессионной TZ сервера ≠ UTC.
Применение: везде `DateTime.UtcNow`, columns `DATETIME(6)`, MySQL в контейнере с `--default-time-zone=+00:00`,
`options.UseMySQL(conn, mysql => mysql.ServerVersion(MySqlServerVersion.Parse("8.0.x")).EnableRetryOnFailure())`.
Refs: https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/commit/6b1e9ca2941fb9c002374e71214c6810c3cf6763,
https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/wiki/Configuration-Options

## 12. ef-concurrency-no-tracking-testdb-and-docker (Medium)
Эталон: EF Core «Handling Concurrency Conflicts» — optimistic concurrency, concurrency token, `DbUpdateConcurrencyException`;
EF Core «Overview of testing applications that use EF Core» — in-memory/SQLite провайдеры для тестов, но с отличиями от
реального провайдера; ASP.NET Core «Integration tests» — WebApplicationFactory + in-memory test server, фабрика создаётся
один раз на fixture (IClassFixture), не на каждый тест; ASP.NET Core «Health checks» — `AddHealthChecks`/`MapHealthChecks`,
Docker `HEALTHCHECK CMD curl --fail http://localhost:5000/health || exit`.
Для проекта: MySQL не имеет нативного rowversion → concurrency token (bigint/uint timestamp или `timestamp` + `IsConcurrencyToken`)
для Edit/Finish WorkDay; интеграционный тест IntegrationApiEndpointsTests.cs:1248 с 242-строчным Program.cs — один
WebApplicationFactory на collection; миграции проверять на реальном MySQL (Docker compose, utf8mb4/TZ), SQLite only как fast-smoke.
Применение:
```csharp
public class ApiTests : IClassFixture<WebApplicationFactory<Program>> { ... }
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();
app.MapHealthChecks("/health");
// Dockerfile: multi-stage (sdk:8.0 → aspnet:8.0), USER non-root, HEALTHCHECK CMD curl --fail http://localhost:8080/health || exit
```
Refs: https://learn.microsoft.com/en-us/ef/core/saving/concurrency?tabs=data-annotations,
https://learn.microsoft.com/en-us/ef/core/testing/, https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/test/integration-tests.md,
https://raw.githubusercontent.com/dotnet/AspNetCore.Docs/main/aspnetcore/host-and-deploy/health-checks.md,
https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks

## Не подтверждено / не цитируется
- RFC 9126 оказалась «OAuth 2.0 Pushed Authorization Requests» (https://www.rfc-editor.org/rfc/rfc9126.txt, HTTP 200) —
  это НЕ RFC про хеширование паролей; эталон параметров Argon2id берётся из OWASP Password Storage Cheat Sheet и
  спецификации Argon2 (соль 16 байт).
- `raw.githubusercontent.com/OWASP/API-Security/master/2023/en/OWASP-API-Security-Top-10-2023.md` и
  `.../editions/2023/en/0x01-Top-10-Entries.md`, `aspnetcore/security/https-required.md` — HTTP 404, поэтому HSTS/HTTPS-redirect
  и точный ранг-лист 2023 не заявлены как подтверждённые; использованы реальные страницы owasp.org/API-Security README и WSTG.
- docker-compose MySQL (utf8mb4/TZ) и Serilog/OpenTelemetry: отдельные официальные страницы не подтверждены чтением;
  рекомендация из п. 11/12 опирается на health-checks и Pomelo wiki.

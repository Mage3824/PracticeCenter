// Startup of WorkTime.Api (Stage 2: Web API + JWT + authorization).
//
// Explicit startup style (WebApplication.CreateBuilder / builder.Build / RunAsync), so the middleware
// order is visible instead of implicit:
//   exception handler -> request logging -> Swagger -> rate limiting -> authentication ->
//   BR32 activity check -> authorization -> controllers.
// DI follows the existing wiring (аудит 6.1):
//   * repositories are SCOPED - one instance per request scope, so a service never resolves a
//     repository from another lifetime; the dev-tier in-memory store stays shared (audit fix),
//     while the interface itself is scoped;
//   * services and TokenService are SCOPED - a fresh instance per request scope;
//   * JwtSettings and UserActivityTracker are SINGLETONS (one signing key, one activity record);
//     the tracker holds only per-user activity time and depends on no repository.
using System.Collections.Generic;
using System.Linq;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using WorkTime.Infrastructure;

namespace WorkTime.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ---- Configuration ---------------------------------------------------------
        // Параметры JWT берутся из конфигурации (SecretKey / Issuer / Audience / ExpiryMinutes).
        // Ключ подписи — только из конфигурации (dotnet user-secrets, переменные окружения,
        // боевой config): пустой, короче 32 байт или равный прежнему dev-ключу из репозитория
        // — приложение падает на старте в любом окружении (аудит 2.1).
        var jwtSettings = JwtSettingsFactory.TryFromConfiguration(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<JwtSettings>(jwtSettings);

        // ---- Настройка логирования (этап 2, чек-лист «Настроить логирование») ----------
        // WebHost по умолчанию на Windows добавляет EventLog-провайдер: он пишет в системный
        // журнал Application и требует прав администратора, поэтому не-возвышённый запуск
        // падает на старте с AggregateException "Cannot open log for source '.NET Runtime'".
        // Снимаем все провайдеры по умолчанию и оставляем встроенное логирование в консоли;
        // уровни берутся из секции Logging из appsettings.json (включая EventLog: None — страховка
        // для случая, когда EventLog вернут явно).
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddConsole();

        // ---- Repositories: dev-tier ("in-memory") implementations, one per interface ----
        // Production (database-backed) repositories replace these through the same wiring in Stage 4.
        // Аудит 6.1: интерфейс репозитория регистрируется как SCOPED — один экземпляр на request-скоуп,
        // поэтому бизнес-сервис (тоже scoped) никогда не резолвит репозиторий чужого lifetime.
        // Dev-tier хранилище при этом остаётся общим на весь процесс (это именно dev-реализация,
        // не DbContext): иначе демо-сид и логин теряли бы записи между запросами.
        builder.Services.AddSingleton<InMemoryUserRepository>();
        builder.Services.AddSingleton<InMemoryDepartmentRepository>();
        // Аудит 6.1: синглтон-хранилище dev-tier получает только синглтон-зависимости —
        // scoped-интерфейс в его конструкторе валидация DI отвергает.
        builder.Services.AddSingleton<InMemoryWorkDayRepository>(provider =>
            new InMemoryWorkDayRepository(provider.GetRequiredService<InMemoryPeriodClosureRepository>()));
        builder.Services.AddSingleton<InMemoryBreakRepository>(provider =>
            new InMemoryBreakRepository(provider.GetRequiredService<InMemoryWorkDayRepository>()));
        builder.Services.AddSingleton<InMemoryReportRepository>();
        builder.Services.AddSingleton<InMemoryCommentRepository>(provider =>
            new InMemoryCommentRepository(provider.GetRequiredService<InMemoryReportRepository>()));
        builder.Services.AddSingleton<InMemoryAbsenceRepository>(provider =>
            new InMemoryAbsenceRepository(provider.GetRequiredService<InMemoryPeriodClosureRepository>()));
        builder.Services.AddSingleton<InMemoryAuditLogRepository>();
        builder.Services.AddSingleton<InMemoryPeriodClosureRepository>();

        builder.Services.AddScoped<IUserRepository>(provider =>
            provider.GetRequiredService<InMemoryUserRepository>());
        builder.Services.AddScoped<IDepartmentRepository>(provider =>
            provider.GetRequiredService<InMemoryDepartmentRepository>());
        builder.Services.AddScoped<IWorkDayRepository>(provider =>
            provider.GetRequiredService<InMemoryWorkDayRepository>());
        builder.Services.AddScoped<IBreakRepository>(provider =>
            provider.GetRequiredService<InMemoryBreakRepository>());
        builder.Services.AddScoped<IReportRepository>(provider =>
            provider.GetRequiredService<InMemoryReportRepository>());
        builder.Services.AddScoped<ICommentRepository>(provider =>
            provider.GetRequiredService<InMemoryCommentRepository>());
        builder.Services.AddScoped<IAbsenceRepository>(provider =>
            provider.GetRequiredService<InMemoryAbsenceRepository>());
        builder.Services.AddScoped<IAuditLogRepository>(provider =>
            provider.GetRequiredService<InMemoryAuditLogRepository>());
        builder.Services.AddScoped<IPeriodClosureRepository>(provider =>
            provider.GetRequiredService<InMemoryPeriodClosureRepository>());

        // ---- Services: scoped (transient) instances -------------------------------------
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<WorkTimeService>();
        builder.Services.AddScoped<BreakService>();
        builder.Services.AddScoped<ReportService>();
        builder.Services.AddScoped<ReportSearchService>();
        builder.Services.AddScoped<AbsenceService>();

        // TokenService issues the JWT of a successful login; it reads the same settings singleton.
        builder.Services.AddScoped<TokenService>();

        // Controllers need the request context (its stop signal and its claims) without declaring
        // framework implicit parameters, so the accessor is registered as a service.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<RequestContextProvider>();

        // ---- Authentication: Bearer JWT (issuer + audience + lifetime + key validated) ----
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => JwtBearerSetup.TryConfigure(options, jwtSettings));

        // Аудит 2.1: жёсткая проверка ключа подписи на старте. Приложение падает здесь, если ключ
        // пуст/whitespace или равен прежнему дефолтному значению из репозитория, — а не при первом
        // выпуске токена.
        JwtSettingsFactory.TryValidateSigningKey(jwtSettings);

        // ---- Rate limiting (аудит 2.2): встроенный rate limiter .NET 8 -------------------
        // Ограничен только вход: 5 разрешений на 60 секунд для одного client IP, очередь пуста
        // (QueueLimit = 0), отказ отдаёт 429 как ProblemDetails без внутренних деталей.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy<string>(
                LoginRateLimitPolicy,
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKeyOf(httpContext),
                    factory: static _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = LoginRateLimitPermits,
                        // Для FixedWindowRateLimiter окно восполнения задаётся свойством Window;
                        // это те самые 60 секунд replenishment периода.
                        Window = LoginRateLimitWindow,
                        QueueLimit = 0,
                    }));

            // Глобальный обработчик (IExceptionHandler) отказ rate limiter'а не видит: запрос
            // не бросает исключение, поэтому ProblemDetails собирается здесь, в том же envelope.
            options.OnRejected = async (context, cancellationToken) =>
            {
                var detail = "[RU] Слишком много попыток входа. Повторите позже.";

                var problem = new ProblemDetails
                {
                    Type = "https://errors.worktime/rate-limit",
                    Title = "Too many requests",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = detail,
                };
                problem.Extensions["code"] = "RATE_LIMITED";
                problem.Extensions["message"] = detail;
                problem.Extensions["httpStatus"] = StatusCodes.Status429TooManyRequests;
                problem.Extensions["error"] = ErrorEnvelope.TryEnvelope(
                    "RATE_LIMITED", detail, StatusCodes.Status429TooManyRequests);

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });

        // ---- Authorization: named policies (AdminOnly / ManagerOrAdmin / EmployeeOnly) ----
        builder.Services.AddAuthorization(options => WorkTimeAuthorizationPolicies.TryConfigure(options));

        // ---- Exception handling: 409 / 404 / 400 / 401 / 500 as ProblemDetails (RFC 7807) ----
        // The developer exception page is deliberately not used: the wire contract is a ProblemDetails
        // body, not an HTML page.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<WorkTimeExceptionHandler>();

        // ---- BR32 activity record + request logging -------------------------------------
        builder.Services.AddSingleton<UserActivityTracker>(provider =>
            new UserActivityTracker(provider.GetRequiredService<JwtSettings>()));
        builder.Services.AddScoped<UserActivityTimeoutMiddleware>();
        builder.Services.AddScoped<RequestLoggingMiddleware>();

        builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
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

                foreach (var error in entryErrors)
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
    });

        // ---- Swagger: the Bearer scheme is defined; the requirement is per endpoint ----
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "WorkTime API",
                Version = "v1",
                Description = "Contract of api-contracts.md (Stage 2): DTOs, JWT identity, business-rule errors."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "JWT Authorization header using the Bearer scheme. Send: Authorization: Bearer <accessToken>"
            });

            // Only endpoints that actually ask for authorization carry the requirement, so the
            // Authorize button works while /api/auth/login and the Swagger document stay anonymous.
            options.OperationFilter<BearerSecurityRequirementOperationFilter>();
        });

        var app = builder.Build();

        // Демо-сид dev-tier: только в Development и только при ручном запуске, до первого запроса,
        // иначе smoke-сценарий Stage 2 (логин -> токен -> рабочий день) не находит ни одной учётной
        // записи. Интеграционные тесты (WebApplicationFactory) сеют свои данные сами: сид добавил бы
        // второй активный администратор, и гейт BR20 перестал бы отказывать деактивацию последнего.
        // Аудит 6.1: репозитории теперь scoped, поэтому сид пишется из request-скоупа (with using),
        // а не из root-провайдера — scoped из singleton-провайдера резолвится с ошибкой.
        if (app.Environment.IsDevelopment() && IsManualRun())
        {
            using var seedScope = app.Services.CreateScope();

            await DevDataSeeder.TrySeedAsync(
                seedScope.ServiceProvider.GetRequiredService<IUserRepository>(),
                seedScope.ServiceProvider.GetRequiredService<IDepartmentRepository>(),
                CancellationToken.None);
        }

        // Wraps the whole pipeline; everything a controller or a service throws becomes ProblemDetails.
        app.UseExceptionHandler();
        app.UseMiddleware<RequestLoggingMiddleware>();

        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "WorkTime API V1"));

        // Аудит 2.2: rate limiter до authentication и authorization — отказанный запрос не должен
        // тратить проверку токена и ролей. В тестовом окружении отключается: интеграционные тесты
        // делают много запросов на /login с одного localhost IP и иначе получают 429 вместо
        // ответа AuthService.
        if (!app.Environment.IsEnvironment("Testing"))
        {
            app.UseRateLimiter();
        }

        app.UseAuthentication();

        // The token is valid but the owner may have been idle for 8 hours - BR32 refuses it.
        app.UseMiddleware<UserActivityTimeoutMiddleware>();

        app.UseAuthorization();

        app.MapControllers();

        await app.RunAsync();
    }

    // Аудит 2.2: политика ограничения входа. Имя держится здесь же, где её регистрация, — контроллер
    // ссылается на него через [EnableRateLimiting(LoginRateLimitPolicy)].
    public const string LoginRateLimitPolicy = "login-rate-limit";

    // 5 попыток входа на 60 секунд для одного client IP; очередь пуста (QueueLimit = 0).
    public const int LoginRateLimitPermits = 5;

    private static readonly TimeSpan LoginRateLimitWindow = TimeSpan.FromSeconds(60);

    // Partition по client IP; запрос без удалённого адреса (тестовый хост, Unix domain socket)
    // собирается в одну partition "unknown".
    private static string PartitionKeyOf(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Ручной запуск приложения: главный сборник процесса — сам WorkTime.Api. Тестовый хост
    // (WebApplicationFactory) запускается из сборника тестов, поэтому dev-сид там не пишется.
    private static bool IsManualRun()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly();
        return entry != null && entry.GetName().Name == typeof(Program).Assembly.GetName().Name;
    }
}

// Adds the Bearer security requirement to every endpoint that is not [AllowAnonymous]. An endpoint
// carrying neither attribute is covered by the authorization fallback policy, so it is documented as
// protected too.
public class BearerSecurityRequirementOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata == null || metadata.Any(item => item is IAllowAnonymous))
        {
            return;
        }

        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer",
            },
        };

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new OpenApiSecurityRequirement { [scheme] = new List<string>() },
        };
    }
}

// Needed by integration tests: WebApplicationFactory<Program> (Stage 2 checklist item).
public partial class Program { }

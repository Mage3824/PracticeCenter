// Интеграционные тесты этапа 2 («API и авторизация», чек-лист roadmap.md строки 32-54):
// реальный HTTP-стек WorkTime.Api через WebApplicationFactory<Program>.
//
// Правила:
//   * токены получают только через реальный POST /api/auth/login (никаких своих генераторов JWT);
//   * сиды (отдел + пользователи + расчётный период) создаются напрямую in-memory репозиториями того же
//     хоста — их concrete-реализации зарегистрированы синглтонами в Program.cs (интерфейсы — scoped
//     поверх них), поэтому это состояние того же приложения, а не подмена production-кода; резолв идёт
//     через CreateScope(), а не из root-провайдера;
//   * формы запросов/ответов берются из api-contracts.md, тесты их не меняют;
//   * каждый тест создаёт свой WebApplicationFactory => свои синглтонные репозитории, тесты не мешают друг другу.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;using Microsoft.Extensions.Logging;using Microsoft.Extensions.Logging.Abstractions;using Microsoft.AspNetCore.TestHost;using Microsoft.AspNetCore.Hosting;
using WorkTime.Api;

namespace WorkTime.Tests.Integration;

// Проводные ответы (camelCase из api-contracts.md). Имена не совпадают с DTO из WorkTime.Application,
// чтобы не создавать неоднозначности с типами глобального пространства имён.
internal sealed class WireLogin
{
    [JsonPropertyName("isSuccess")] public bool IsSuccess { get; set; }
    [JsonPropertyName("requiresCaptcha")] public bool RequiresCaptcha { get; set; }
    [JsonPropertyName("isLocked")] public bool IsLocked { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("lockedUntil")] public string? LockedUntil { get; set; }
    [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
    [JsonPropertyName("expiresIn")] public long? ExpiresIn { get; set; }
}

internal sealed class WireWorkDay
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("userId")] public Guid UserId { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
    [JsonPropertyName("startTime")] public string StartTime { get; set; } = string.Empty;
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("countedInReports")] public bool CountedInReports { get; set; }
    [JsonPropertyName("isBreakLimitExceeded")] public bool IsBreakLimitExceeded { get; set; }
}

internal sealed class WireBreak
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("workDayId")] public Guid WorkDayId { get; set; }
    [JsonPropertyName("startTime")] public string StartTime { get; set; } = string.Empty;
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("durationMinutes")] public long? DurationMinutes { get; set; }
    [JsonPropertyName("countedInReports")] public bool CountedInReports { get; set; }
}

internal sealed class WireReportBreak
{
    [JsonPropertyName("startTime")] public string StartTime { get; set; } = string.Empty;
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
}

internal sealed class WireDailyReport
{
    [JsonPropertyName("startTime")] public string StartTime { get; set; } = string.Empty;
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("breaks")] public List<WireReportBreak> Breaks { get; set; } = new();
    [JsonPropertyName("totalMinutes")] public long TotalMinutes { get; set; }
}

internal sealed class WireMonthlyReport
{
    [JsonPropertyName("employeeName")] public string EmployeeName { get; set; } = string.Empty;
    [JsonPropertyName("hoursByDay")] public Dictionary<string, long> HoursByDay { get; set; } = new();
    [JsonPropertyName("totalMinutes")] public long TotalMinutes { get; set; }
    [JsonPropertyName("absenceDays")] public List<string> AbsenceDays { get; set; } = new();
}

internal sealed class WireReport
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("workDayId")] public Guid WorkDayId { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
}

internal sealed class WireComment
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("reportId")] public Guid ReportId { get; set; }
    [JsonPropertyName("managerId")] public Guid ManagerId { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = string.Empty;
}

internal sealed class WireUser
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
    [JsonPropertyName("fullName")] public string FullName { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("departmentId")] public Guid DepartmentId { get; set; }
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("failedLoginAttempts")] public int FailedLoginAttempts { get; set; }
    [JsonPropertyName("lockedUntil")] public string? LockedUntil { get; set; }
    [JsonPropertyName("locked")] public bool Locked { get; set; }
}

internal sealed class WireAbsence
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("userId")] public Guid UserId { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
    [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("createdByUserId")] public string? CreatedByUserId { get; set; }
}

internal sealed class WirePeriodClosure
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("departmentId")] public Guid DepartmentId { get; set; }
    [JsonPropertyName("year")] public int Year { get; set; }
    [JsonPropertyName("month")] public int Month { get; set; }
    [JsonPropertyName("isClosed")] public bool IsClosed { get; set; }
    [JsonPropertyName("closedAt")] public string? ClosedAt { get; set; }
    [JsonPropertyName("closedBy")] public string? ClosedBy { get; set; }
    [JsonPropertyName("reopenReason")] public string? ReopenReason { get; set; }
    [JsonPropertyName("reopenedByUserId")] public string? ReopenedByUserId { get; set; }
}

internal sealed class WireCanStart
{
    [JsonPropertyName("canStart")] public bool CanStart { get; set; }
}

// Хост WorkTime.Api + сиды + получением токенов через /api/auth/login.
internal sealed class ApiHarness : IDisposable
{
    private const string AdminLogin = "integration.admin";
    private const string AdminPassword = "Admin-password-1";
    private const string ManagerLogin = "integration.manager";
    private const string ManagerPassword = "Manager-password-1";
    private const string EmployeeLogin = "integration.employee";
    private const string EmployeePassword = "Employee-password-1";
    private const string Employee2Login = "integration.employee2";
    private const string Employee2Password = "Employee2-password-1";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public Guid DepartmentId { get; private set; }
    public Guid AdminId { get; private set; }
    public Guid ManagerId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid Employee2Id { get; private set; }

    public string AdminToken { get; private set; } = string.Empty;
    public string ManagerToken { get; private set; } = string.Empty;
    public string EmployeeToken { get; private set; } = string.Empty;
    public string Employee2Token { get; private set; } = string.Empty;

    public static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private ApiHarness(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public static async Task<ApiHarness> CreateAsync()
    {
        // Тестовый ключ подписи: только для интеграционного контура, через переменную окружения.
        // Стартовая проверка JwtSettings требует ключ >= 32 байт и не равный прежнему dev-ключу,
        // а боевой ключ в репозитории лежать не должен.
        Environment.SetEnvironmentVariable("JwtSettings__SecretKey", "integration-test-signing-key-0123456789abcdef0123456789abcdef");
        // Тестовое окружение "Testing": rate limiting на /login отключён (см. Program.cs), иначе
        // тесты на BR35 (несколько неверных паролей подряд) получают 429 вместо ответа AuthService.
        var harness = new ApiHarness(new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(s => { var logDescriptor = s.FirstOrDefault(d => d.ServiceType == typeof(ILoggerFactory)); if (logDescriptor != null) { s.Remove(logDescriptor); } s.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance); });
        }));
        await harness.SeedAsync();
        harness.AdminToken = await harness.LoginAsync(AdminLogin, AdminPassword);
        harness.ManagerToken = await harness.LoginAsync(ManagerLogin, ManagerPassword);
        harness.EmployeeToken = await harness.LoginAsync(EmployeeLogin, EmployeePassword);
        harness.Employee2Token = await harness.LoginAsync(Employee2Login, Employee2Password);
        return harness;
    }

    // Root IServiceProvider того же хоста: синглтон UserActivityTracker; репозитории теперь
    // scoped и резолвятся из request-скоупа, а не из root-провайдера.
    public System.IServiceProvider FactoryServices => _factory.Services;

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task SeedAsync()
    {
        DepartmentId = Guid.NewGuid();
        AdminId = Guid.NewGuid();
        ManagerId = Guid.NewGuid();
        EmployeeId = Guid.NewGuid();
        Employee2Id = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var departments = scope.ServiceProvider.GetRequiredService<IDepartmentRepository>();
        var cancellationToken = CancellationToken.None;

        await departments.SaveAsync(
            new Department { Id = DepartmentId, Name = "Integration department" }, cancellationToken);

        await users.SaveAsync(NewUser(AdminId, AdminLogin, AdminPassword, "Root Administrator", UserRole.Admin), cancellationToken);
        await users.SaveAsync(NewUser(ManagerId, ManagerLogin, ManagerPassword, "Department Manager", UserRole.Manager), cancellationToken);
        await users.SaveAsync(NewUser(EmployeeId, EmployeeLogin, EmployeePassword, "Ivan Petrov", UserRole.Employee), cancellationToken);
        await users.SaveAsync(NewUser(Employee2Id, Employee2Login, Employee2Password, "Petr Ivanov", UserRole.Employee), cancellationToken);
    }

    private User NewUser(Guid id, string login, string password, string fullName, UserRole role)
    {
        // BR34: пароль пишется хешем PasswordHasher — тот же, который сверяет AuthService.
        return new User
        {
            Id = id,
            Login = login,
            PasswordHash = PasswordHasher.TryHash(password),
            FullName = fullName,
            Role = role,
            DepartmentId = DepartmentId,
            IsActive = true,
            FailedLoginAttempts = 0,
            Locked = false,
            LockedUntil = null,
        };
    }

    public async Task<PeriodClosure> SeedPeriodClosureAsync(int year, int month, bool isClosed = false)
    {
        using var scope = _factory.Services.CreateScope();
        var closures = scope.ServiceProvider.GetRequiredService<IPeriodClosureRepository>();

        var closure = new PeriodClosure
        {
            Id = Guid.NewGuid(),
            DepartmentId = DepartmentId,
            Year = year,
            Month = month,
            IsClosed = isClosed,
        };

        await closures.SaveAsync(closure, CancellationToken.None);
        return closure;
    }

    // BR32: единственная точка «активности» — синглтонный UserActivityTracker того же хоста.
    public void BackdateActivity(Guid userId, TimeSpan inactiveFor)
    {
        var tracker = _factory.Services.GetRequiredService<UserActivityTracker>();
        tracker.TryRecordActivity(userId.ToString(), DateTime.UtcNow - inactiveFor);
    }

    public async Task<string> LoginAsync(string login, string password)
    {
        var response = await PostAsync("/api/auth/login", new { login, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var wire = await ReadAsync<WireLogin>(response);
        Assert.True(wire.IsSuccess, "login must succeed: " + wire.Message);
        Assert.NotNull(wire.AccessToken);

        return wire.AccessToken!;
    }

    public Task<HttpResponseMessage> GetAsync(string url, string? token = null)
        => SendAsync(HttpMethod.Get, url, null, token);

    public Task<HttpResponseMessage> PostAsync(string url, object? body, string? token = null)
        => SendAsync(HttpMethod.Post, url, body, token);

    public Task<HttpResponseMessage> PutAsync(string url, object? body, string? token = null)
        => SendAsync(HttpMethod.Put, url, body, token);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, string? token)
    {
        var request = new HttpRequestMessage(method, url);

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(text);

        if (value == null)
        {
            throw new InvalidOperationException("Cannot read JSON body: " + text);
        }

        return value;
    }

    // ProblemDetails (RFC 7807) + машинные ключи как extensions (code/message/httpStatus).
    public static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }

    public static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    public static int Number(JsonElement element, string name) => element.GetProperty(name).GetInt32();
}

public class IntegrationApiEndpointsTests
{
    // ---- 1) Авторизация (BR35/BR36) ----------------------------------------------------------

    [Fact]
    public async Task Login_Success_Returns200_WithJwt_And_Expiry()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var response = await api.PostAsync("/api/auth/login",
                new { login = "integration.admin", password = "Admin-password-1" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var wire = await ApiHarness.ReadAsync<WireLogin>(response);
            Assert.True(wire.IsSuccess);
            Assert.Equal(string.Empty, wire.Message);
            Assert.False(wire.IsLocked);
            Assert.False(wire.RequiresCaptcha);

            // JWT = три сегмента; дальше он принимается защищённым эндпоинтом (валидация JwtBearer).
            Assert.NotNull(wire.AccessToken);
            Assert.Equal(3, wire.AccessToken!.Split('.').Length);
            Assert.Equal(480 * 60, wire.ExpiresIn!.Value);

            var protectedResponse = await api.GetAsync("/api/work-days/current", wire.AccessToken!);
            Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
            // Открытой смены нет: §2 требует 200 + literal "null" (application/json), не пустое тело.
            Assert.Equal("application/json", protectedResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("null", await protectedResponse.Content.ReadAsStringAsync());
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsFailureFlags_NoToken_And_CountsAttempt()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var first = await api.PostAsync("/api/auth/login",
                new { login = "integration.manager", password = "wrong-password" });

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var firstWire = await ApiHarness.ReadAsync<WireLogin>(first);
            Assert.False(firstWire.IsSuccess);
            Assert.Null(firstWire.AccessToken);

            // BR36: со второй неудачи для руководителя/администратора требуется CAPTCHA.
            var second = await api.PostAsync("/api/auth/login",
                new { login = "integration.manager", password = "wrong-password" });
            var secondWire = await ApiHarness.ReadAsync<WireLogin>(second);
            Assert.False(secondWire.IsSuccess);
            Assert.True(secondWire.RequiresCaptcha);
            Assert.Null(secondWire.AccessToken);

            // Счётчик неудач виден администратору (GET /api/users/{id}).
            var userResponse = await api.GetAsync("/api/users/" + api.ManagerId.ToString("D"), api.AdminToken);
            Assert.Equal(HttpStatusCode.OK, userResponse.StatusCode);
            var user = await ApiHarness.ReadAsync<WireUser>(userResponse);
            Assert.Equal(2, user.FailedLoginAttempts);

            // Неудачный вход не даёт токен: защищённый эндпоинт остаётся закрытым.
            var anonymous = await api.GetAsync("/api/users/" + api.ManagerId.ToString("D"));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // Правильный пароль обнуляет счётчик (BR35).
            var okToken = await api.LoginAsync("integration.manager", "Manager-password-1");
            Assert.NotNull(okToken);
            var afterSuccess = await ApiHarness.ReadAsync<WireUser>(
                await api.GetAsync("/api/users/" + api.ManagerId.ToString("D"), api.AdminToken));
            Assert.Equal(0, afterSuccess.FailedLoginAttempts);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Login_DeactivatedAccount_IsRefused_BR24()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var deactivated = await api.PostAsync("/api/users/" + api.EmployeeId.ToString("D") + "/deactivate", null, api.AdminToken);
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

            var login = await api.PostAsync("/api/auth/login",
                new { login = "integration.employee", password = "Employee-password-1" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            var wire = await ApiHarness.ReadAsync<WireLogin>(login);
            Assert.False(wire.IsSuccess);
            Assert.True(wire.IsLocked);
            Assert.Null(wire.AccessToken);
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 2) Запрос без токена ---------------------------------------------------------------

    [Fact]
    public async Task RequestWithoutToken_Returns401()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var workDay = await api.GetAsync("/api/work-days/current");
            Assert.Equal(HttpStatusCode.Unauthorized, workDay.StatusCode);

            var users = await api.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.Unauthorized, users.StatusCode);

            var reports = await api.GetAsync("/api/reports/daily/" + Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.Unauthorized, reports.StatusCode);

            // Login — единственный анонимный эндпоинт.
            var login = await api.PostAsync("/api/auth/login",
                new { login = "integration.employee", password = "Employee-password-1" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 3) Политика ролей: Employee не проходит на админские эндпоинты ---------------------

    [Fact]
    public async Task EmployeeToken_IsForbidden_OnAdminAndManagerEndpoints_403()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var createUser = await api.PostAsync("/api/users",
                new { login = "new.user", fullName = "New User", password = "pw-1", role = "Employee", departmentId = api.DepartmentId },
                api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Forbidden, createUser.StatusCode);

            var deactivate = await api.PostAsync("/api/users/" + api.Employee2Id.ToString("D") + "/deactivate", null, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);

            var monthly = await api.GetAsync("/api/reports/monthly", api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Forbidden, monthly.StatusCode);

            var setAbsence = await api.PostAsync("/api/absences/" + api.Employee2Id.ToString("D"),
                new { date = ApiHarness.Today, reason = "Sick" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Forbidden, setAbsence.StatusCode);

            var addComment = await api.PostAsync("/api/reports/" + Guid.NewGuid().ToString("D") + "/comments",
                new { reportId = Guid.NewGuid(), text = "note" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Forbidden, addComment.StatusCode);

            // EmployeeOnly пропускной endpoint работает для самого сотрудника.
            var canStart = await api.GetAsync("/api/work-days/" + api.EmployeeId.ToString("D") + "/can-start?date=" + ApiHarness.Today, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.OK, canStart.StatusCode);

            // Manager читает пользователей, но не создаёт их (AdminOnly).
            var managerList = await api.GetAsync("/api/users", api.ManagerToken);
            Assert.Equal(HttpStatusCode.OK, managerList.StatusCode);

            var managerCreate = await api.PostAsync("/api/users",
                new { login = "new.user2", fullName = "New User 2", password = "pw-2", role = "Employee", departmentId = api.DepartmentId },
                api.ManagerToken);
            Assert.Equal(HttpStatusCode.Forbidden, managerCreate.StatusCode);

            // Admin проходит.
            var adminCreate = await api.PostAsync("/api/users",
                new { login = "new.user3", fullName = "New User 3", password = "pw-3", role = "Employee", departmentId = api.DepartmentId },
                api.AdminToken);
            Assert.Equal(HttpStatusCode.OK, adminCreate.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 4) Полный сценарий рабочего дня (BR1-BR11) -----------------------------------------

    private static async Task<WireWorkDay> StartDayAsync(ApiHarness api, string startTime = "09:00")
    {
        var response = await api.PostAsync("/api/work-days/start",
            new { date = ApiHarness.Today, startTime }, api.EmployeeToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiHarness.ReadAsync<WireWorkDay>(response);
    }

    private static async Task<WireBreak> StartBreakAsync(ApiHarness api, Guid workDayId, string startTime)
    {
        var response = await api.PostAsync("/api/work-days/" + workDayId.ToString("D") + "/breaks/start",
            new { startTime }, api.EmployeeToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiHarness.ReadAsync<WireBreak>(response);
    }

    private static async Task<WireBreak> FinishBreakAsync(ApiHarness api, Guid breakId, string endTime)
    {
        var response = await api.PostAsync("/api/breaks/" + breakId.ToString("D") + "/finish",
            new { endTime }, api.EmployeeToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiHarness.ReadAsync<WireBreak>(response);
    }

    [Fact]
    public async Task WorkDay_FullScenario_Start_Breaks_Finish_Current_Daily_BR1_To_BR11()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await StartDayAsync(api);
            Assert.Equal(api.EmployeeId, day.UserId);
            Assert.Equal(ApiHarness.Today, day.Date);
            Assert.Equal("09:00", day.StartTime);
            Assert.Null(day.EndTime);
            Assert.Equal("Active", day.Status);
            Assert.False(day.IsBreakLimitExceeded);

            // GET /current показывает открытую смену того же сотрудника.
            var current = await ApiHarness.ReadAsync<WireWorkDay>(
                await api.GetAsync("/api/work-days/current", api.EmployeeToken));
            Assert.Equal(day.Id, current.Id);

            // Перерыв 10:00-12:00 — считается (BR11).
            var break1 = await StartBreakAsync(api, day.Id, "10:00");
            var finished1 = await FinishBreakAsync(api, break1.Id, "12:00");
            Assert.Equal(120, finished1.DurationMinutes!.Value);
            Assert.True(finished1.CountedInReports);

            // Перерыв 12:05-12:08 (3 минуты) — молча не считается (BR8).
            var break2 = await StartBreakAsync(api, day.Id, "12:05");
            var finished2 = await FinishBreakAsync(api, break2.Id, "12:08");
            Assert.Equal(3, finished2.DurationMinutes!.Value);
            Assert.False(finished2.CountedInReports);

            // Перерыв 12:10-16:20 (4 ч 10 мин) — превышение лимита это флаг, не ошибка (BR9).
            var break3 = await StartBreakAsync(api, day.Id, "12:10");
            var finished3 = await FinishBreakAsync(api, break3.Id, "16:20");
            Assert.Equal(250, finished3.DurationMinutes!.Value);

            var finishedDay = await ApiHarness.ReadAsync<WireWorkDay>(
                await api.PostAsync("/api/work-days/" + day.Id.ToString("D") + "/finish", new { endTime = "17:00" }, api.EmployeeToken));
            Assert.Equal("Finished", finishedDay.Status);
            Assert.Equal("17:00", finishedDay.EndTime);
            Assert.True(finishedDay.IsBreakLimitExceeded);
            Assert.True(finishedDay.CountedInReports);

            // После закрытия открытой смены нет: 200 + literal "null" как /api/reports/daily (§2).
            var currentAfter = await api.GetAsync("/api/work-days/current", api.EmployeeToken);
            Assert.Equal(HttpStatusCode.OK, currentAfter.StatusCode);
            Assert.Equal("application/json", currentAfter.Content.Headers.ContentType?.MediaType);
            Assert.Equal("null", await currentAfter.Content.ReadAsStringAsync());

            // Отчёт дня: учитываются только считанные перерывы (BR8/BR11).
            var daily = await ApiHarness.ReadAsync<WireDailyReport>(
                await api.GetAsync("/api/reports/daily/" + day.Id.ToString("D"), api.EmployeeToken));
            Assert.Equal("09:00", daily.StartTime);
            Assert.Equal("17:00", daily.EndTime);
            Assert.Equal(3, daily.Breaks.Count);
            Assert.Equal(480 - 120 - 250, daily.TotalMinutes);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task WorkDay_SecondStart_SameDate_Conflict_BR1()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            await StartDayAsync(api);

            var again = await api.PostAsync("/api/work-days/start",
                new { date = ApiHarness.Today, startTime = "10:00" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

            var problem = await ApiHarness.ReadProblemAsync(again);
            Assert.Equal("BR1", ApiHarness.Text(problem, "code"));
            Assert.Equal(409, ApiHarness.Number(problem, "status"));
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Break_CannotStart_After_DayFinished_Conflict_BR6()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await StartDayAsync(api);
            await api.PostAsync("/api/work-days/" + day.Id.ToString("D") + "/finish", new { endTime = "10:00" }, api.EmployeeToken);

            var breakResponse = await api.PostAsync("/api/work-days/" + day.Id.ToString("D") + "/breaks/start",
                new { startTime = "10:30" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, breakResponse.StatusCode);
            Assert.Equal("BR6", ApiHarness.Text(await ApiHarness.ReadProblemAsync(breakResponse), "code"));
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Break_SecondOpen_Conflict_BR10()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await StartDayAsync(api);
            var first = await StartBreakAsync(api, day.Id, "10:00");

            var second = await api.PostAsync("/api/work-days/" + day.Id.ToString("D") + "/breaks/start",
                new { startTime = "11:00" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("BR10", ApiHarness.Text(await ApiHarness.ReadProblemAsync(second), "code"));

            // После закрытия первого перерыва второй старт проходит.
            await FinishBreakAsync(api, first.Id, "10:30");
            var reopened = await api.PostAsync("/api/work-days/" + day.Id.ToString("D") + "/breaks/start",
                new { startTime = "11:00" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Break_ExceedingFourHours_SetsFlag_NotError_BR9()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await StartDayAsync(api);
            var first = await StartBreakAsync(api, day.Id, "10:00");
            await FinishBreakAsync(api, first.Id, "12:00");

            var second = await StartBreakAsync(api, day.Id, "12:10");
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/work-days/current", api.EmployeeToken)).StatusCode);

            var finished = await FinishBreakAsync(api, second.Id, "16:30");
            Assert.Equal(260, finished.DurationMinutes!.Value);

            var current = await ApiHarness.ReadAsync<WireWorkDay>(
                await api.GetAsync("/api/work-days/current", api.EmployeeToken));
            Assert.True(current.IsBreakLimitExceeded);
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 5) Отчёты (BR13-BR16, BR19/BR21) ----------------------------------------------------

    private static async Task<WireWorkDay> FinishedDayAsync(ApiHarness api, string token, string startTime = "09:00", string endTime = "17:00")
    {
        var start = await api.PostAsync("/api/work-days/start",
            new { date = ApiHarness.Today, startTime }, token);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var day = await ApiHarness.ReadAsync<WireWorkDay>(start);

        var finish = await api.PostAsync("/api/work-days/" + day.Id.ToString("D") + "/finish",
            new { endTime }, token);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        return await ApiHarness.ReadAsync<WireWorkDay>(finish);
    }

    [Fact]
    public async Task DailyReport_CannotBeCreated_While_DayOpen_BR14()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await StartDayAsync(api);

            var response = await api.PostAsync("/api/reports/" + day.Id.ToString("D"),
                new { text = "draft" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("BR14", ApiHarness.Text(await ApiHarness.ReadProblemAsync(response), "code"));
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Report_EmployeeCreatesOwn_Then_OnlyAdminEdits_AfterDayEnd_BR12()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await FinishedDayAsync(api, api.EmployeeToken);

            var created = await ApiHarness.ReadAsync<WireReport>(
                await api.PostAsync("/api/reports/" + day.Id.ToString("D"), new { text = "Worked 4h" }, api.EmployeeToken));
            Assert.Equal(day.Id, created.WorkDayId);
            Assert.Equal("Worked 4h", created.Text);

            // После окончания дня правка доступна только администратору (BR12).
            var employeeEdit = await api.PostAsync("/api/reports/" + day.Id.ToString("D"),
                new { text = "changed by employee" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, employeeEdit.StatusCode);
            Assert.Equal("BR12", ApiHarness.Text(await ApiHarness.ReadProblemAsync(employeeEdit), "code"));

            var managerEdit = await api.PostAsync("/api/reports/" + day.Id.ToString("D"),
                new { text = "changed by manager" }, api.ManagerToken);
            Assert.Equal(HttpStatusCode.Conflict, managerEdit.StatusCode);

            var adminEdit = await ApiHarness.ReadAsync<WireReport>(
                await api.PostAsync("/api/reports/" + day.Id.ToString("D"), new { text = "changed by admin" }, api.AdminToken));
            Assert.Equal("changed by admin", adminEdit.Text);
            Assert.NotNull(adminEdit.UpdatedAt);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task DailyReport_EmployeeSeesOnlyOwnDay_ManagerSeesForeign_BR21()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await FinishedDayAsync(api, api.EmployeeToken);

            var own = await api.GetAsync("/api/reports/daily/" + day.Id.ToString("D"), api.EmployeeToken);
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);

            var foreign = await api.GetAsync("/api/reports/daily/" + day.Id.ToString("D"), api.Employee2Token);
            Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
            Assert.Equal("BR21", ApiHarness.Text(await ApiHarness.ReadProblemAsync(foreign), "code"));

            var manager = await api.GetAsync("/api/reports/daily/" + day.Id.ToString("D"), api.ManagerToken);
            Assert.Equal(HttpStatusCode.OK, manager.StatusCode);

            var editForeign = await api.PostAsync("/api/reports/" + day.Id.ToString("D"),
                new { text = "foreign edit" }, api.Employee2Token);
            Assert.Equal(HttpStatusCode.Conflict, editForeign.StatusCode);
            Assert.Equal("BR21", ApiHarness.Text(await ApiHarness.ReadProblemAsync(editForeign), "code"));
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task ManagerComment_AddedAndListed_BR13()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var day = await FinishedDayAsync(api, api.EmployeeToken);
            var report = await ApiHarness.ReadAsync<WireReport>(
                await api.PostAsync("/api/reports/" + day.Id.ToString("D"), new { text = "day summary" }, api.EmployeeToken));

            var comment = await ApiHarness.ReadAsync<WireComment>(
                await api.PostAsync("/api/reports/" + day.Id.ToString("D") + "/comments",
                    new { reportId = report.Id, text = "check overtime" }, api.ManagerToken));
            Assert.Equal(report.Id, comment.ReportId);
            Assert.Equal(api.ManagerId, comment.ManagerId);
            Assert.Equal("check overtime", comment.Text);

            var listed = await ApiHarness.ReadAsync<List<WireComment>>(
                await api.GetAsync("/api/reports/" + day.Id.ToString("D") + "/comments", api.AdminToken));
            Assert.Single(listed);

            // Комментарий к чужому рабочему дню с несовпадающим reportId — 400.
            var mismatch = await api.PostAsync("/api/reports/" + Guid.NewGuid().ToString("D") + "/comments",
                new { reportId = report.Id, text = "x" }, api.ManagerToken);
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task MonthlyReport_ForManagerAndAdmin_WithRowsAndAbsences_BR14_BR15_BR16()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var closure = await api.SeedPeriodClosureAsync(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
            var day = await FinishedDayAsync(api, api.EmployeeToken);


            var url = "/api/reports/monthly?periodClosureId=" + closure.Id.ToString("D")
                + "&departmentId=" + api.DepartmentId.ToString("D");

            var asManager = await ApiHarness.ReadAsync<List<WireMonthlyReport>>(await api.GetAsync(url, api.ManagerToken));
            var row = asManager.Single(item => item.EmployeeName == "Ivan Petrov");
            Assert.Equal(480, row.TotalMinutes);
            Assert.Equal(480, row.HoursByDay[ApiHarness.Today]);
            Assert.Empty(row.AbsenceDays);

            var asAdmin = await api.GetAsync(url, api.AdminToken);
            Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);

            var asEmployee = await api.GetAsync(url, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Forbidden, asEmployee.StatusCode);

            // BR26: отсутствие дня попадает в месячный отчёт.
            await api.PostAsync("/api/absences/" + api.Employee2Id.ToString("D"),
                new { date = ApiHarness.Today, reason = "DayOff" }, api.ManagerToken);
            var afterAbsence = await ApiHarness.ReadAsync<List<WireMonthlyReport>>(await api.GetAsync(url, api.ManagerToken));
            var absentRow = afterAbsence.Single(item => item.EmployeeName == "Petr Ivanov");
            Assert.Equal(new List<string> { ApiHarness.Today }, absentRow.AbsenceDays);
            Assert.False(absentRow.HoursByDay.ContainsKey(ApiHarness.Today));
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 6) Пользователи: деактивация, последний админ, разблокировка -----------------------

    [Fact]
    public async Task DeactivatedUser_StaysInList_WithInactiveFlag_BR24()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var deactivated = await ApiHarness.ReadAsync<WireUser>(
                await api.PostAsync("/api/users/" + api.EmployeeId.ToString("D") + "/deactivate", null, api.AdminToken));
            Assert.False(deactivated.IsActive);

            var activeList = await ApiHarness.ReadAsync<List<WireUser>>(await api.GetAsync("/api/users", api.AdminToken));
            Assert.DoesNotContain(activeList, item => item.Id == api.EmployeeId);

            var fullList = await ApiHarness.ReadAsync<List<WireUser>>(
                await api.GetAsync("/api/users?includeInactive=true", api.AdminToken));
            Assert.Contains(fullList, item => item.Id == api.EmployeeId);

            // Разблокировка доступа (isActive=true).
            var reactivated = await ApiHarness.ReadAsync<WireUser>(
                await api.PutAsync("/api/users/" + api.EmployeeId.ToString("D"), new { isActive = true }, api.AdminToken));
            Assert.True(reactivated.IsActive);

            var token = await api.LoginAsync("integration.employee", "Employee-password-1");
            Assert.NotNull(token);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task DeactivateLastActiveAdmin_IsRefused_BR20()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var response = await api.PostAsync("/api/users/" + api.AdminId.ToString("D") + "/deactivate", null, api.AdminToken);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("BR20", ApiHarness.Text(await ApiHarness.ReadProblemAsync(response), "code"));

            var removed = await api.GetAsync("/api/users/" + api.AdminId.ToString("D"), null);
            Assert.Equal(HttpStatusCode.Unauthorized, removed.StatusCode);

            // Смена роли последнего администратора тоже запрещена (BR20).
            var roleChange = await api.PutAsync("/api/users/" + api.AdminId.ToString("D"),
                new { role = "Manager" }, api.AdminToken);
            Assert.Equal(HttpStatusCode.Conflict, roleChange.StatusCode);

            var stillActive = await ApiHarness.ReadAsync<WireUser>(
                await api.GetAsync("/api/users/" + api.AdminId.ToString("D"), api.AdminToken));
            Assert.Equal("Admin", stillActive.Role);
            Assert.True(stillActive.IsActive);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task AdminUnlock_ResetsLockoutCount_BR35()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            await api.PostAsync("/api/auth/login", new { login = "integration.manager", password = "bad-1" }, null);
            await api.PostAsync("/api/auth/login", new { login = "integration.manager", password = "bad-2" }, null);

            var before = await ApiHarness.ReadAsync<WireUser>(
                await api.GetAsync("/api/users/" + api.ManagerId.ToString("D"), api.AdminToken));
            Assert.Equal(2, before.FailedLoginAttempts);

            var unlocked = await ApiHarness.ReadAsync<WireUser>(
                await api.PostAsync("/api/users/" + api.ManagerId.ToString("D") + "/unlock", null, api.AdminToken));
            Assert.Equal(0, unlocked.FailedLoginAttempts);
            Assert.False(unlocked.Locked);
            Assert.Null(unlocked.LockedUntil);
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 7) Отсутствие (BR27, BR28) ---------------------------------------------------------

    [Fact]
    public async Task Absence_BlocksWorkDayStart_BR27_And_ReusesSameRecord_BR28()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var created = await ApiHarness.ReadAsync<WireAbsence>(
                await api.PostAsync("/api/absences/" + api.EmployeeId.ToString("D"),
                    new { date = ApiHarness.Today, reason = "Sick" }, api.ManagerToken));
            Assert.Equal(api.EmployeeId, created.UserId);
            Assert.Equal(ApiHarness.Today, created.Date);
            Assert.Equal("Sick", created.Reason);
            Assert.Equal(api.ManagerId.ToString("D"), created.CreatedByUserId);

            var canStart = await ApiHarness.ReadAsync<WireCanStart>(
                await api.GetAsync("/api/work-days/" + api.EmployeeId.ToString("D") + "/can-start?date=" + ApiHarness.Today, api.EmployeeToken));
            Assert.False(canStart.CanStart);

            var start = await api.PostAsync("/api/work-days/start",
                new { date = ApiHarness.Today, startTime = "09:00" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
            Assert.Equal("BR27", ApiHarness.Text(await ApiHarness.ReadProblemAsync(start), "code"));

            // PUT того же дня: id переиспользуется (BR28).
            var updated = await ApiHarness.ReadAsync<WireAbsence>(
                await api.PutAsync("/api/absences/" + api.EmployeeId.ToString("D"),
                    new { date = ApiHarness.Today, reason = "Leave" }, api.AdminToken));
            Assert.Equal(created.Id, updated.Id);
            Assert.Equal("Leave", updated.Reason);

            var read = await ApiHarness.ReadAsync<List<WireAbsence>>(
                await api.GetAsync("/api/absences/" + api.EmployeeId.ToString("D") + "?date=" + ApiHarness.Today, api.ManagerToken));
            Assert.Single(read);
            Assert.Equal("Leave", read[0].Reason);

            // Второй сотрудник в тот же день не заблокирован — старт проходит.
            var otherStart = await api.PostAsync("/api/work-days/start",
                new { date = ApiHarness.Today, startTime = "09:00" }, api.Employee2Token);
            Assert.Equal(HttpStatusCode.OK, otherStart.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task Employee_CanCheckCanStart_OnlyForSelf_BR21()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var response = await api.GetAsync("/api/work-days/" + api.Employee2Id.ToString("D") + "/can-start?date=" + ApiHarness.Today, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("BR21", ApiHarness.Text(await ApiHarness.ReadProblemAsync(response), "code"));
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 8) Валидация запросов (ProblemDetails RFC 7807) ------------------------------------

    [Fact]
    public async Task InvalidRequestBody_Returns400_ProblemDetails()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var badTime = await api.PostAsync("/api/work-days/start",
                new { date = ApiHarness.Today, startTime = "25:00" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.BadRequest, badTime.StatusCode);

            var problem = await ApiHarness.ReadProblemAsync(badTime);
            Assert.NotNull(ApiHarness.Text(problem, "type"));
            Assert.NotNull(ApiHarness.Text(problem, "title"));
            Assert.Equal(400, ApiHarness.Number(problem, "status"));

            var badDate = await api.PostAsync("/api/work-days/start",
                new { date = "11/03/2026", startTime = "09:00" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);

            var missingLogin = await api.PostAsync("/api/users",
                new { fullName = "No Login", password = "pw", role = "Employee", departmentId = api.DepartmentId }, api.AdminToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingLogin.StatusCode);
            var loginProblem = await ApiHarness.ReadProblemAsync(missingLogin);
            Assert.Equal(400, ApiHarness.Number(loginProblem, "status"));

            var missingRole = await api.PostAsync("/api/users",
                new { login = "roleless.user", fullName = "Roleless", password = "pw", role = "Supervisor", departmentId = api.DepartmentId }, api.AdminToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingRole.StatusCode);
            Assert.Equal("ROLE_INVALID", ApiHarness.Text(await ApiHarness.ReadProblemAsync(missingRole), "code"));

            var missingText = await api.PostAsync("/api/reports/" + Guid.NewGuid().ToString("D"), new { }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingText.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task InvalidAbsenceReason_Returns400_WithMachineCode()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var response = await api.PostAsync("/api/absences/" + api.EmployeeId.ToString("D"),
                new { date = ApiHarness.Today, reason = "Unknown" }, api.ManagerToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await ApiHarness.ReadProblemAsync(response);
            Assert.Equal("https://errors.worktime/invalid-request", ApiHarness.Text(problem, "type"));
                        Assert.Equal("Request rejected", ApiHarness.Text(problem, "title"));
            Assert.Equal(400, ApiHarness.Number(problem, "status"));
            Assert.Equal("REASON_INVALID", ApiHarness.Text(problem, "code"));
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 9) Закрытие и вскрытие расчётного периода (BR23.1, BR23.2) ------------------------

    [Fact]
    public async Task PeriodClosure_CloseBlocksEditing_ReopenRequiresReason_BR23()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var closure = await api.SeedPeriodClosureAsync(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
            var closePath = "/api/period-closures/" + closure.Id.ToString("D") + "/close";
            var reopenPath = "/api/period-closures/" + closure.Id.ToString("D") + "/reopen";

            var closed = await ApiHarness.ReadAsync<WirePeriodClosure>(await api.PostAsync(closePath, null, api.AdminToken));
            Assert.True(closed.IsClosed);
            Assert.NotNull(closed.ClosedAt);
            Assert.Equal(api.AdminId.ToString("D"), closed.ClosedBy);

            // BR23.1: повторное закрытие того же периода запрещено.
            var reclose = await api.PostAsync(closePath, null, api.AdminToken);
            Assert.Equal(HttpStatusCode.Conflict, reclose.StatusCode);
            Assert.Equal("BR23.1", ApiHarness.Text(await ApiHarness.ReadProblemAsync(reclose), "code"));

            // Вне политики: руководитель не закрывает периоды.
            var byManager = await api.PostAsync(closePath, null, api.ManagerToken);
            Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode);

            // BR23.2: вскрытие без причины — 400.
            var noReason = await api.PostAsync(reopenPath, new { }, api.AdminToken);
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            var reasonProblem = await ApiHarness.ReadProblemAsync(noReason);
            Assert.Equal(400, ApiHarness.Number(reasonProblem, "status"));
            Assert.NotNull(ApiHarness.Text(reasonProblem, "title"));

            var reopened = await ApiHarness.ReadAsync<WirePeriodClosure>(
                await api.PostAsync(reopenPath, new { reason = "Act #7: late correction" }, api.AdminToken));
            Assert.False(reopened.IsClosed);
            Assert.Equal("Act #7: late correction", reopened.ReopenReason);
            Assert.Equal(api.AdminId.ToString("D"), reopened.ReopenedByUserId);

            // Вскрытие уже открытого периода запрещено (BR23.2).
            var reopenOpen = await api.PostAsync(reopenPath, new { reason = "again" }, api.AdminToken);
            Assert.Equal(HttpStatusCode.Conflict, reopenOpen.StatusCode);
            Assert.Equal("BR23.2", ApiHarness.Text(await ApiHarness.ReadProblemAsync(reopenOpen), "code"));

            // Блокировка редактирования после закрытия дня реализована как BR12 в ReportService
            // (см. тест Report_EmployeeCreatesOwn_Then_OnlyAdminEdits_AfterDayEnd_BR12).
            var day = await FinishedDayAsync(api, api.EmployeeToken);
            await api.PostAsync("/api/reports/" + day.Id.ToString("D"), new { text = "draft" }, api.EmployeeToken);
            var employeeEdit = await api.PostAsync("/api/reports/" + day.Id.ToString("D"),
                new { text = "late edit" }, api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Conflict, employeeEdit.StatusCode);
            Assert.Equal("BR12", ApiHarness.Text(await ApiHarness.ReadProblemAsync(employeeEdit), "code"));

            var adminEdit = await api.PostAsync("/api/reports/" + day.Id.ToString("D"),
                new { text = "admin correction" }, api.AdminToken);
            Assert.Equal(HttpStatusCode.OK, adminEdit.StatusCode);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task PeriodClosure_UnknownId_Returns404_ProblemDetails()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var response = await api.PostAsync("/api/period-closures/" + Guid.NewGuid().ToString("D") + "/close", null, api.AdminToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await ApiHarness.ReadProblemAsync(response);
            Assert.Equal(404, ApiHarness.Number(problem, "status"));
            Assert.Equal("PeriodClosure", ApiHarness.Text(problem, "entityType"));
        }
        finally
        {
            api.Dispose();
        }
    }

    // ---- 10) Middleware BR32: 8 часов без активности -----------------------------------------

    [Fact]
    public async Task SessionExpired_After8HoursOfInactivity_Returns401_BR32()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            var first = await api.GetAsync("/api/work-days/current", api.EmployeeToken);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            // Открытой смены нет -> 200 + literal "null" (§2).
            Assert.Equal("application/json", first.Content.Headers.ContentType?.MediaType);
            Assert.Equal("null", await first.Content.ReadAsStringAsync());

            // Единственный поддерживаемый способ сдвинуть часы: синглтонный UserActivityTracker
            // пишет активность сам middleware; тест откатывает её на 9 часов назад.
            api.BackdateActivity(api.EmployeeId, TimeSpan.FromHours(9));

            var expired = await api.GetAsync("/api/work-days/current", api.EmployeeToken);
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
            Assert.Equal("Bearer", expired.Headers.WwwAuthenticate.ToString());

            var problem = await ApiHarness.ReadProblemAsync(expired);
            Assert.Equal("https://errors.worktime/session-expired", ApiHarness.Text(problem, "type"));
            Assert.Equal("Session expired", ApiHarness.Text(problem, "title"));
            Assert.Equal(401, ApiHarness.Number(problem, "status"));
            Assert.Equal("SESSION_EXPIRED", ApiHarness.Text(problem, "code"));

            // Новый вход снимает блокировку BR32.
            var freshToken = await api.LoginAsync("integration.employee", "Employee-password-1");
            var afterRelogin = await api.GetAsync("/api/work-days/current", freshToken);
            Assert.Equal(HttpStatusCode.OK, afterRelogin.StatusCode);
            // Открытой смены нет -> 200 + literal "null" (§2).
            Assert.Equal("application/json", afterRelogin.Content.Headers.ContentType?.MediaType);
            Assert.Equal("null", await afterRelogin.Content.ReadAsStringAsync());

            // Лимит BR32 = lifetime токена (8 часов).
            var tracker = api.FactoryServices.GetRequiredService<UserActivityTracker>();
            Assert.Equal(TimeSpan.FromHours(8), tracker.InactivityLimit);
        }
        finally
        {
            api.Dispose();
        }
    }

    [Fact]
    public async Task ActivityThatDoesNotFailKeepsSessionAlive_BR32()
    {
        var api = await ApiHarness.CreateAsync();

        try
        {
            api.BackdateActivity(api.EmployeeId, TimeSpan.FromHours(9));
            api.BackdateActivity(api.EmployeeId, TimeSpan.Zero);

            var response = await api.GetAsync("/api/work-days/current", api.EmployeeToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // Открытой смены нет -> 200 + literal "null" (§2).
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("null", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            api.Dispose();
        }
    }
}

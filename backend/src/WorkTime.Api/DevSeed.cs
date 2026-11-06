// Демо-сид dev-tier (in-memory) репозиториев для ручного запуска и smoke-проверки Stage 2.
//
// Без учётных записей приложение запускается, но ни один сценарий не проверяется: логин не
// находит пользователя, а защищённые эндпоинты не получают токен. Сид заполняет хранилище
// однократно при старте хоста, только в Development и только если оно пустое — интеграционные
// тесты сеют свои учётные записи сами и не зависят от этих.
//
// PasswordHash совпадает с паролем: хешер появляется на этапе 4 (AuthService.HashPasswordPlaceholder
// пока возвращает пароль как есть), поэтому сид работает ровно так же, как тестовые данные.
using WorkTime.Domain;

namespace WorkTime.Api;

public static class DevDataSeeder
{
    // Фиксированные id: smoke-проверка и Swagger UI ссылаются на них, а не на случайные Guid.
    public static readonly Guid DepartmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid ManagerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public const string AdminLogin = "admin";
    public const string ManagerLogin = "manager";
    public const string EmployeeLogin = "employee";

    public const string AdminPassword = "admin-password-1";
    public const string ManagerPassword = "manager-password-1";
    public const string EmployeePassword = "employee-password-1";

    // Заполняет department + три роли (Admin / Manager / Employee). Идемпотентно:
    // если в хранилище уже есть пользователи, ничего не добавляется.
    public static async Task TrySeedAsync(
        IUserRepository users,
        IDepartmentRepository departments,
        CancellationToken cancellationToken)
    {
        if (users == null || departments == null)
        {
            return;
        }

        if ((await users.GetAllAsync(cancellationToken)).Any())
        {
            return;
        }

        await departments.SaveAsync(
            new Department { Id = DepartmentId, Name = "Плановый отдел", ParentId = null },
            cancellationToken);

        await users.SaveAsync(
            NewUser(AdminId, AdminLogin, AdminPassword, "Администратор системы", UserRole.Admin),
            cancellationToken);

        await users.SaveAsync(
            NewUser(ManagerId, ManagerLogin, ManagerPassword, "Руководитель отдела", UserRole.Manager),
            cancellationToken);

        await users.SaveAsync(
            NewUser(EmployeeId, EmployeeLogin, EmployeePassword, "Иван Петров", UserRole.Employee),
            cancellationToken);
    }

    private static User NewUser(Guid id, string login, string password, string fullName, UserRole role)
    {
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
}

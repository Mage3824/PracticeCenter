// Пользователи: CRUD, деактивация (BR24), разблокировка (BR35).
// Права по BR19: запись — только администратор; чтение — руководитель и администратор.
// Ответы никогда не содержат PasswordHash (BR34).
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkTime.Domain;

namespace WorkTime.Api;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _users;

    private readonly IDepartmentRepository _departments;

    private readonly IAuditLogRepository _auditLogs;

    private readonly RequestContextProvider _requestContext;

    public UsersController(
        IUserRepository users,
        IDepartmentRepository departments,
        IAuditLogRepository auditLogs,
        RequestContextProvider requestContext)
    {
        _users = users;
        _departments = departments;
        _auditLogs = auditLogs;
        _requestContext = requestContext;
    }

    [HttpGet("")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserWire[]))]
    public async Task<UserWire[]> TryListUsers([FromQuery] string? name, [FromQuery] bool includeInactive)
    {
        var cancellationToken = _requestContext.TryStopSignal();

        // BR24: деактивированные учётные записи остаются, поэтому список по умолчанию активный,
        // а полный список (includeInactive) нужен администратору для обслуживания.
        if (includeInactive)
        {
            return ManagementWireConversion.TryFromUsers(await _users.GetAllAsync(cancellationToken));
        }

        var found = await _users.GetActiveByNameAsync(name ?? string.Empty, cancellationToken);
        return ManagementWireConversion.TryFromUsers(found.Values);
    }

    [HttpGet("{userId}")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserWire))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<UserWire> TryGetUser(Guid userId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var user = await TryRequireUserAsync(userId, cancellationToken);
        return ManagementWireConversion.TryFromUser(user);
    }

    [HttpPost("")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<UserWire> TryCreateUser(CreateUserRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var login = RequestParsers.TryRequireText(payload.login, "login");
        var fullName = RequestParsers.TryRequireText(payload.fullName, "fullName");
        var password = RequestParsers.TryRequireText(payload.password, "password");
        var role = RequestParsers.TryParseEnum<UserRole>(payload.role, "role", "ROLE_INVALID");
        var departmentId = payload.departmentId ?? throw new InvalidRequestException(
            "DEPARTMENT_ID_REQUIRED",
            "[RU] Field 'departmentId' is required.");

        var department = await _departments.GetByIdAsync(departmentId, cancellationToken);
        if (department == null)
        {
            throw new EntityNotFoundException(departmentId, "Department");
        }

        var taken = await _users.GetByLoginAsync(login, cancellationToken);
        if (taken != null)
        {
            throw new BusinessRuleException("USER_LOGIN_TAKEN", "[RU] This login is already used by another account.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            // BR34: пароль пишется хешем PasswordHasher — открытый пароль не храним.
            PasswordHash = PasswordHasher.TryHash(password),
            FullName = fullName,
            Role = role,
            DepartmentId = departmentId,
            IsActive = true,
            FailedLoginAttempts = 0,
            Locked = false,
            LockedUntil = null,
        };

        await _users.SaveAsync(user, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Create, "User", user.Id, null, TryUserSummary(user), cancellationToken);

        return ManagementWireConversion.TryFromUser(user);
    }

    [HttpPut("{userId}")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<UserWire> TryUpdateUser(Guid userId, UpdateUserRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var user = await TryRequireUserAsync(userId, cancellationToken);
        var before = TryUserSummary(user);

        // Значения готовятся до правки: сначала синтаксические ошибки (400), потом мутация.
        var nextRole = payload.role == null
            ? user.Role
            : RequestParsers.TryParseEnum<UserRole>(payload.role, "role", "ROLE_INVALID");
        var nextIsActive = payload.isActive ?? user.IsActive;

        Guid? nextDepartmentId = null;
        if (payload.departmentId != null)
        {
            var department = await _departments.GetByIdAsync(payload.departmentId.Value, cancellationToken);
            if (department == null)
            {
                throw new EntityNotFoundException(payload.departmentId.Value, "Department");
            }

            nextDepartmentId = payload.departmentId.Value;
        }

        // BR20: правка снимает роль администратора или деактивирует его (состояние ДО правки).
        var removesActiveAdmin = user.Role == UserRole.Admin && user.IsActive
            && (nextRole != UserRole.Admin || !nextIsActive);

        // Правка аудита 3.2/3.3: мьютекс учётных записей убран, гейт BR20 и запись идут через await.
        if (removesActiveAdmin)
        {
            await TryGuardLastActiveAdminAsync(user.Id, user.Role, user.IsActive, cancellationToken);
        }

        if (payload.fullName != null)
        {
            user.FullName = RequestParsers.TryRequireText(payload.fullName, "fullName");
        }

        if (nextDepartmentId != null)
        {
            user.DepartmentId = nextDepartmentId.Value;
        }

        user.Role = nextRole;
        user.IsActive = nextIsActive;

        await _users.SaveAsync(user, cancellationToken);

        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Update, "User", user.Id, before, TryUserSummary(user), cancellationToken);

        return ManagementWireConversion.TryFromUser(user);
    }

    [HttpDelete("{userId}")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task TryDeleteUser(Guid userId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var user = await TryRequireUserAsync(userId, cancellationToken);

        // BR20: удаление последнего активного администратора запрещено.
        // Правка аудита 3.2/3.3: мьютекс убран, проверка и удаление идут через await.
        await TryGuardLastActiveAdminAsync(user.Id, user.Role, user.IsActive, cancellationToken);

        await _users.DeleteAsync(userId, cancellationToken);

        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Delete, "User", userId, TryUserSummary(user), null, cancellationToken);
    }

    // BR24: блокировка доступа при сохранении учётной записи и всех исторических данных.
    [HttpPost("{userId}/deactivate")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserWire))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<UserWire> TryDeactivateUser(Guid userId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var user = await TryRequireUserAsync(userId, cancellationToken);

        // BR20: деактивация последнего активного администратора запрещена (BR24 для остальных).
        // Правка аудита 3.2/3.3: мьютекс убран, проверка и запись идут через await.
        await TryGuardLastActiveAdminAsync(user.Id, user.Role, user.IsActive, cancellationToken);

        user.IsActive = false;
        await _users.SaveAsync(user, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs,
            actorId,
            AuditAction.Update,
            "User",
            userId,
            "isActive=true",
            "isActive=false (BR24)",
            cancellationToken);

        return ManagementWireConversion.TryFromUser(user);
    }

    // BR35: досрочная разблокировка — право администратора.
    [HttpPost("{userId}/unlock")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UserWire))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<UserWire> TryUnlockUser(Guid userId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var user = await TryRequireUserAsync(userId, cancellationToken);
        var before = "failedLoginAttempts=" + user.FailedLoginAttempts + ", locked=" + user.Locked;

        user.FailedLoginAttempts = 0;
        user.Locked = false;
        user.LockedUntil = null;

        await _users.SaveAsync(user, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Update, "User", userId, before, "unlocked (BR35)", cancellationToken);

        return ManagementWireConversion.TryFromUser(user);
    }

    private async Task<User> TryRequireUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new EntityNotFoundException(userId, "User");
        }

        return user;
    }

    // BR20: в системе должен остаться хотя бы один активный администратор. Состояние передаётся
    // ДО правки. Правка аудита 3.2/3.3: метод полностью асинхронный (без блокирующих вызовов),
    // мьютекс учётных записей убран.
    private async Task TryGuardLastActiveAdminAsync(
        Guid targetId,
        UserRole role,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (role != UserRole.Admin || !isActive)
        {
            return;
        }

        var activeAdmins = 0;
        foreach (var candidate in await _users.GetAllAsync(cancellationToken))
        {
            if (candidate.Id != targetId && candidate.Role == UserRole.Admin && candidate.IsActive)
            {
                activeAdmins++;
            }
        }

        if (activeAdmins > 0)
        {
            return;
        }

        throw new BusinessRuleException(
            "BR20",
            "[RU] The action would leave the system with no active administrator (BR20).");
    }

    private static string TryUserSummary(User user)
    {
        return "login=" + user.Login + ", role=" + user.Role + ", departmentId=" + user.DepartmentId
            + ", isActive=" + user.IsActive;
    }
}

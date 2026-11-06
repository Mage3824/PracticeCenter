// Отделы: CRUD (BR19 — настройка системы belongs to the administrator; BR23.1 — закрытие
// расчётного периода настраивается по отделу, поэтому дерево отделов доступно руководителю на чтение).
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkTime.Domain;

namespace WorkTime.Api;

[ApiController]
[Route("api/departments")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentRepository _departments;

    private readonly IUserRepository _users;

    private readonly IAuditLogRepository _auditLogs;

    private readonly RequestContextProvider _requestContext;

    public DepartmentsController(
        IDepartmentRepository departments,
        IUserRepository users,
        IAuditLogRepository auditLogs,
        RequestContextProvider requestContext)
    {
        _departments = departments;
        _users = users;
        _auditLogs = auditLogs;
        _requestContext = requestContext;
    }

    [HttpGet("")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DepartmentWire[]))]
    public async Task<DepartmentWire[]> TryListDepartments()
    {
        var cancellationToken = _requestContext.TryStopSignal();
        return ManagementWireConversion.TryFromDepartments(await _departments.GetAllAsync(cancellationToken));
    }

    [HttpGet("{departmentId}")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DepartmentWire))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DepartmentWire> TryGetDepartment(Guid departmentId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        return ManagementWireConversion.TryFromDepartment(
            await TryRequireDepartmentAsync(departmentId, cancellationToken));
    }

    [HttpGet("{departmentId}/children")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.ManagerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DepartmentWire[]))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DepartmentWire[]> TryListChildren(Guid departmentId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        await TryRequireDepartmentAsync(departmentId, cancellationToken);
        return ManagementWireConversion.TryFromDepartments(
            await _departments.GetChildrenOfAsync(departmentId, cancellationToken));
    }

    [HttpPost("")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DepartmentWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DepartmentWire> TryCreateDepartment(CreateDepartmentRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var name = RequestParsers.TryRequireText(payload.name, "name");
        var parentId = payload.parentId;

        if (parentId != null)
        {
            await TryRequireDepartmentAsync(parentId.Value, cancellationToken);
        }

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentId = parentId,
        };

        await _departments.SaveAsync(department, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Create, "Department", department.Id, null, "name=" + department.Name,
            cancellationToken);

        return ManagementWireConversion.TryFromDepartment(department);
    }

    [HttpPut("{departmentId}")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DepartmentWire))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<DepartmentWire> TryUpdateDepartment(Guid departmentId, UpdateDepartmentRequest payload)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var department = await TryRequireDepartmentAsync(departmentId, cancellationToken);
        var before = "name=" + department.Name + ", parentId=" + department.ParentId;

        if (payload.name != null)
        {
            department.Name = RequestParsers.TryRequireText(payload.name, "name");
        }

        if (payload.parentId != null)
        {
            if (payload.parentId.Value == departmentId)
            {
                throw new InvalidRequestException(
                    "SELF_PARENT",
                    "[RU] A department cannot be its own parent.");
            }

            await TryRequireDepartmentAsync(payload.parentId.Value, cancellationToken);
            department.ParentId = payload.parentId.Value;
        }

        await _departments.SaveAsync(department, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Update, "Department", department.Id, before,
            "name=" + department.Name + ", parentId=" + department.ParentId, cancellationToken);

        return ManagementWireConversion.TryFromDepartment(department);
    }

    [HttpDelete("{departmentId}")]
    [Authorize(Policy = WorkTimeAuthorizationPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task TryDeleteDepartment(Guid departmentId)
    {
        var cancellationToken = _requestContext.TryStopSignal();
        var actorId = RequestIdentity.TrySubjectUserId(_requestContext.TryContext());
        await RequestIdentity.TryRequireKnownUserAsync(_users, actorId, cancellationToken);

        var department = await TryRequireDepartmentAsync(departmentId, cancellationToken);

        // Удаление отдела с подчинёнными оставило бы их ParentId висящим — сначала дерево.
        var children = await _departments.GetChildrenOfAsync(departmentId, cancellationToken);
        foreach (var unused in children)
        {
            throw new BusinessRuleException(
                "DEPARTMENT_HAS_CHILDREN",
                "[RU] Move or delete the subordinate departments before deleting this one.");
        }

        var employees = await _users.GetActiveEmployeesInDepartmentAsync(departmentId, cancellationToken);
        foreach (var unused in employees)
        {
            throw new BusinessRuleException(
                "DEPARTMENT_HAS_EMPLOYEES",
                "[RU] Move the employees of this department to another department first.");
        }

        await _departments.DeleteAsync(departmentId, cancellationToken);
        await AuditTrail.TryWriteAsync(
            _auditLogs, actorId, AuditAction.Delete, "Department", departmentId, "name=" + department.Name, null,
            cancellationToken);
    }

    private async Task<Department> TryRequireDepartmentAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var department = await _departments.GetByIdAsync(departmentId, cancellationToken);
        if (department == null)
        {
            throw new EntityNotFoundException(departmentId, "Department");
        }

        return department;
    }
}

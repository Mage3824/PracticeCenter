// Repository interfaces (Application layer).
// Every method is asynchronous and takes an explicit CancellationToken.
//
// Addressing rule: a repository answers a question about *criteria* (an id, a date,
// a period, an owner) and returns whatever it finds. Callers never assemble the
// answer themselves, so no service method accepts a list of entity ids that only
// the caller knows. GetAllByIdsAsync stays available as a batch-fetch primitive for
// flows that already hold concrete ids (for example bulk administration actions).
using WorkTime.Domain;
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, User>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    Task<Dictionary<String, User>> GetActiveByNameAsync(String name, CancellationToken cancellationToken);

    // Exact login match (no substring search), for the login screen.
    Task<User?> GetByLoginAsync(String login, CancellationToken cancellationToken);

    // Active users of one department (BR16 rows are chosen by the repository, never
    // assembled by a caller). Guid.Empty means "every active user of the system".
    Task<IEnumerable<User>> GetActiveEmployeesInDepartmentAsync(
        Guid departmentId,
        CancellationToken ct = default);

// Stage 2 administration needs the whole account list: BR24 keeps deactivated accounts
    // visible, so a read-all path belongs to the repository contract.
    Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken);
    Task SaveAsync(User user, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Department>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    // Departments directly under the given parent (the hierarchy walk for BR16/BR19 grouping).
    Task<IEnumerable<Department>> GetChildrenOfAsync(Guid parentId, CancellationToken ct = default);

// The administration controller enumerates the department tree; without this the top-level
    // departments (ParentId = null) are unreachable through GetChildrenOfAsync.
    Task<IEnumerable<Department>> GetAllAsync(CancellationToken cancellationToken);
    Task SaveAsync(Department department, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IWorkDayRepository
{
    Task<WorkDay?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, WorkDay>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    // Finished days (EndTime set and Status not Active) whose calendar date falls
    // inside the inclusive window [startDate, endDate]. The window is how a service
    // asks for "the billing period" without receiving day ids.
    Task<IEnumerable<WorkDay>> GetFinishedDaysForPeriodAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);

    // Every working day of one employee (open or finished) inside the inclusive
    // window [startDate, endDate].
    Task<IEnumerable<WorkDay>> GetForEmployeeInPeriodAsync(Guid employeeId, DateTime startDate, DateTime endDate, CancellationToken ct = default);

    Task<Dictionary<Guid, WorkDay>> GetActiveForEmployeeAsync(Guid employeeId, Guid periodClosureId, CancellationToken cancellationToken);

    // Open (not yet finished) working days of one employee on one calendar date. This is
    // the BR1 question - "is a shift already open for this employee today?" - and it
    // answers it without any help from a period closure.
    Task<Dictionary<Guid, WorkDay>> GetActiveForEmployeeOnDateAsync(
        Guid employeeId,
        DateTime date,
        CancellationToken ct = default);

    Task SaveAsync(WorkDay workDay, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IBreakRepository
{
    Task<Break?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Break>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Break>> GetUnfinishedForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    // Every break of one working day, open or finished. This is the query that lets
    // a service total and list a day's breaks without being handed their ids.
    Task<IEnumerable<Break>> GetByWorkDayIdAsync(Guid workDayId, CancellationToken ct = default);

    Task SaveAsync(Break workBreak, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IReportRepository
{
    Task<Report?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Report>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    // The report of a working day (at most one per day), so a service can find and
    // update it without being handed a report id.
    Task<Report?> GetForWorkDayAsync(Guid workDayId, CancellationToken ct = default);

    Task SaveAsync(Report report, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICommentRepository
{
    Task<Comment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Comment>> GetAllForWorkDayAsync(Guid workDayId, CancellationToken cancellationToken);

    Task SaveAsync(Comment comment, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAbsenceRepository
{
    Task<Absence?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Absence>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    Task<Dictionary<Guid, Absence>> GetForPeriodClosureAsync(Guid periodClosureId, CancellationToken cancellationToken);

    // Absences dated inside the inclusive window [startDate, endDate] of any employee.
    Task<IEnumerable<Absence>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);

    // Absences of one employee dated on one calendar date (BR26/BR27 lookup).
    Task<IEnumerable<Absence>> GetForEmployeeOnDateAsync(Guid employeeId, DateTime date, CancellationToken ct = default);

    Task SaveAsync(Absence absence, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAuditLogRepository
{
    Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, AuditLog>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    // BR30: the change history of one entity (who changed what, in which order), so a
    // service can prove that an action was logged without being handed log ids.
    Task<IEnumerable<AuditLog>> GetHistoryForEntityAsync(Guid entityId, CancellationToken ct = default);

    Task SaveAsync(AuditLog auditLog, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IPeriodClosureRepository
{
    Task<PeriodClosure?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Dictionary<Guid, PeriodClosure>> GetAllByIdsAsync(Dictionary<Guid, Guid> ids, CancellationToken cancellationToken);

    Task<PeriodClosure?> GetActiveForDepartmentAsync(Guid departmentId, CancellationToken cancellationToken);

    // Every closure this department ever had (open and closed), so the administrator can
    // find the closed one that BR23.2 has to reopen.
    Task<Dictionary<Guid, PeriodClosure>> GetAllForDepartmentAsync(
        Guid departmentId,
        CancellationToken cancellationToken);

    // The closure standing for one calendar month of one department (BR18 export of an
    // arbitrary period). Null when the department has no closure for that month.
    Task<PeriodClosure?> GetForDepartmentOnMonthAsync(
        Guid departmentId,
        int year,
        int month,
        CancellationToken cancellationToken);

    Task SaveAsync(PeriodClosure periodClosure, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

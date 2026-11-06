// Shared enumerations for the WorkTime domain.
// Standard C# (.NET 8) - no external dependencies.

public enum UserRole
{
    Admin,
    Manager,
    Employee,
}

public enum WorkDayStatus
{
    Active,
    Finished,
    AutoFinished,
}

public enum AbsenceReason
{
    Sick,
    Leave,
    DayOff,
    Absence,
}

public enum AuditAction
{
    Create,
    Update,
    Delete,
}

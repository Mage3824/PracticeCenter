// Application-layer DTO for handing a user record to the frontend (login screen,
// administration list). PasswordHash is deliberately NOT part of the handover: BR34
// forbids plain-text passwords and the hash itself has no reason to leave the system.
public class UserDto
{
    public Guid Id { get; set; }

    public string Login { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public Guid DepartmentId { get; set; }

    // BR24: a deactivated account is preserved but access is blocked.
    public bool IsActive { get; set; }

    // BR35/BR36: the UI shows the countdown to the lock and the CAPTCHA prompt.
    public int FailedLoginAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    // True when the account is blocked until the end of the day (10 failed attempts).
    public bool Locked { get; set; }
}

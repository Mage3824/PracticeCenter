// Application-layer DTO returned by AuthService.LoginAsync (BR35, BR36).
// Message carries the human-readable notification: the countdown to the lock, the
// CAPTCHA prompt, or the reason of a blocked account. It is optional information;
// callers that only care about the three flags can ignore it.
public class AuthResult
{
    public bool IsSuccess { get; set; }

    // BR36: after 2 wrong passwords every next attempt must supply a CAPTCHA.
    public bool RequiresCaptcha { get; set; }

    // BR35: account is temporarily locked (15 minutes) or blocked until the end of
    // the day after 10 failed attempts.
    public bool IsLocked { get; set; }

    public string Message { get; set; } = string.Empty;

    // Moment from which a lock stops blocking the next attempt (null when no lock).
    public DateTime? LockedUntil { get; set; }
}

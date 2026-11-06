// Service that logs a user in. Rules covered: BR35 (after 5 wrong passwords in a
// row the account is locked for 15 minutes; the user is notified from the 2nd wrong
// password on with a countdown to the lock; after 10 failed attempts per day the
// account is blocked until the end of the day and only an administrator can unlock
// it) and BR36 (after 2 wrong passwords each next attempt must carry a CAPTCHA).
// Both rules apply to managers and administrators only.
//
// The service never throws for a failed login: it returns AuthResult so that the UI
// can show the countdown, the lock, or the CAPTCHA prompt.
using WorkTime.Domain;

public class AuthService
{
    private readonly IUserRepository _users;

    public AuthService(IUserRepository users)
    {
        _users = users;
    }

    // BR35: wrong passwords in a row that trigger the temporary 15 minute lock.
    private const int WrongPasswordsToLock = 5;

    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResult> LoginAsync(
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await _users.GetByLoginAsync(login, cancellationToken);
        if (user == null)
        {
            return new AuthResult
            {
                IsSuccess = false,
                RequiresCaptcha = false,
                IsLocked = false,
                Message = "[RU] No account with this login.",
                LockedUntil = null,
            };
        }

        // BR24: a deactivated account keeps its data but access is blocked.
        if (!user.IsActive)
        {
            return new AuthResult
            {
                IsSuccess = false,
                RequiresCaptcha = false,
                IsLocked = true,
                Message = "[RU] Access to the system is blocked for this account.",
                LockedUntil = null,
            };
        }

        var now = DateTime.Now;

        // The account is still inside a lock window (BR35: 15 minutes, or until the
        // end of the day after WorkTimeRules.MaxFailedAttempts wrong passwords).
        if (user.LockedUntil != null && now < user.LockedUntil)
        {
            return new AuthResult
            {
                IsSuccess = false,
                RequiresCaptcha = false,
                IsLocked = true,
                Message = "[RU] Account is locked. Try again later.",
                LockedUntil = user.LockedUntil,
            };
        }

        if (TryMatchesPassword(user, password))
        {
            // Правка аудита 3.2/3.3: мьютекс и sync-over-async убраны, сбчёт счётчика через await.
            user.FailedLoginAttempts = 0;
            user.Locked = false;
            user.LockedUntil = null;
            await _users.SaveAsync(user, cancellationToken);

            return new AuthResult
            {
                IsSuccess = true,
                RequiresCaptcha = false,
                IsLocked = false,
                Message = string.Empty,
                LockedUntil = null,
            };
        }

        // Wrong password: count the attempt and apply BR35/BR36 for managers and
        // administrators only.
        var requiresCaptcha = false;
        var isLocked = false;
        DateTime? lockedUntil = null;
        string message;

        // Правка аудита 3.2/3.3: мьютекс и sync-over-async убраны; счётчик и блокировка
        // правятся тем же асинхронным путём вызова, бизнес-правила BR35/BR36 не меняются.
        user.FailedLoginAttempts = user.FailedLoginAttempts + 1;

        var lockRulesApply = user.Role == UserRole.Admin || user.Role == UserRole.Manager;

        if (lockRulesApply && user.FailedLoginAttempts >= WorkTimeRules.MaxFailedAttempts)
        {
            // BR35: blocked until the end of the day; only an administrator unlocks
            // it early (user.Locked stays true).
            user.Locked = true;
            lockedUntil = EndOfCalendarDayFor(now);
            user.LockedUntil = lockedUntil;
            isLocked = true;
            message = "[RU] Account blocked until the end of the day. Only an administrator can unlock it.";
        }
        else if (lockRulesApply && user.FailedLoginAttempts >= WrongPasswordsToLock)
        {
            // BR35: locked for 15 minutes.
            lockedUntil = now + LockDuration;
            user.LockedUntil = lockedUntil;
            isLocked = true;
            message = "[RU] Account is locked for 15 minutes.";
        }
        else if (lockRulesApply && user.FailedLoginAttempts >= WorkTimeRules.CaptchaThreshold)
        {
            // BR36 + the BR35 countdown notification from the 2nd wrong password on.
            requiresCaptcha = true;
            var remaining = WrongPasswordsToLock - user.FailedLoginAttempts;
            message = "[RU] Wrong password. CAPTCHA required. " + remaining + " attempt(s) before the lock.";
        }
        else
        {
            message = "[RU] Wrong password.";
        }

        await _users.SaveAsync(user, cancellationToken);

        return new AuthResult
        {
            IsSuccess = false,
            RequiresCaptcha = requiresCaptcha,
            IsLocked = isLocked,
            Message = message,
            LockedUntil = lockedUntil,
        };
    }

    // BR34: passwords are stored hashed, so the comparison runs against the stored
    // PBKDF2 hash (PasswordHasher), compared in constant time.
    private static bool TryMatchesPassword(User user, string password)
    {
        return PasswordHasher.TryVerify(password, user.PasswordHash);
    }

    // The moment that ends the current calendar day (23:59:59).
    private static DateTime EndOfCalendarDayFor(DateTime now)
    {
        return now.Date + TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
    }
}

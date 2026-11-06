// xUnit tests for AuthService (src/WorkTime.Application/Services/AuthService.cs).
// Covered rules: BR35 (five wrong passwords in a row lock the account for 15 minutes, and
// while it is locked no further attempt is even counted) and BR36 (from the second wrong
// password on, each next attempt must carry a CAPTCHA). Both rules apply to administrators
// and managers only - an employee gets neither the countdown nor the lock.
//
// LoginAsync never throws for a failed login: it answers with AuthResult so the UI can show
// the countdown, the lock, or the CAPTCHA prompt, which is why these tests assert on state.

public class AuthServiceTests
{
    [Fact]
    public async Task Login_CaptchaRequiredFromSecondWrongPasswordOn()
    {
        // Arrange
        var ct = CancellationToken.None;

        var users = new InMemoryUserRepository();
        var service = new AuthService(users);

        var managerId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = managerId,
                Login = "manager",
                // BR34: хеш вместо открытого пароля; "secret" — сам пароль при сверке входа.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Maria Manager",
                Role = UserRole.Manager,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        // Act: BR36 - two wrong passwords in a row.
        var firstAttempt = await service.LoginAsync("manager", "wrong password", ct);
        var secondAttempt = await service.LoginAsync("manager", "wrong password", ct);

        // Assert: the countdown starts at the second failure, still no lock.
        Assert.False(firstAttempt.IsSuccess);
        Assert.False(firstAttempt.RequiresCaptcha);
        Assert.False(firstAttempt.IsLocked);

        Assert.False(secondAttempt.IsSuccess);
        Assert.True(secondAttempt.RequiresCaptcha);
        Assert.False(secondAttempt.IsLocked);

        var stored = await users.GetByLoginAsync("manager", ct);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.FailedLoginAttempts);
    }

    [Fact]
    public async Task Login_FifthWrongPasswordLocksAccountForFifteenMinutes()
    {
        // Arrange
        var ct = CancellationToken.None;

        var users = new InMemoryUserRepository();
        var service = new AuthService(users);

        var adminId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = adminId,
                Login = "admin",
                // BR34: хеш вместо открытого пароля; "secret" — сам пароль при сверке входа.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ada Administrator",
                Role = UserRole.Admin,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        // Act: BR35 - five wrong passwords in a row.
        AuthResult fifthAttempt = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            fifthAttempt = await service.LoginAsync("admin", "wrong password", ct);
        }

        // Assert: the account is locked and the lock window is reported back.
        Assert.False(fifthAttempt.IsSuccess);
        Assert.True(fifthAttempt.IsLocked);
        Assert.True(fifthAttempt.LockedUntil != null);
        Assert.False(fifthAttempt.RequiresCaptcha);

        var storedAfterLock = await users.GetByLoginAsync("admin", ct);
        Assert.NotNull(storedAfterLock);
        Assert.Equal(5, storedAfterLock.FailedLoginAttempts);
        Assert.True(storedAfterLock.LockedUntil != null);

        // While the lock is in force even a correct password does not log in and the
        // failed-attempt counter is not advanced.
        var attemptInsideLock = await service.LoginAsync("admin", "secret", ct);

        Assert.False(attemptInsideLock.IsSuccess);
        Assert.True(attemptInsideLock.IsLocked);

        var storedAfterBlockedAttempt = await users.GetByLoginAsync("admin", ct);
        Assert.NotNull(storedAfterBlockedAttempt);
        Assert.Equal(5, storedAfterBlockedAttempt.FailedLoginAttempts);
    }

    [Fact]
    public async Task Login_EmployeeIsNeverLockedOrAskedForCaptcha()
    {
        // Arrange
        var ct = CancellationToken.None;

        var users = new InMemoryUserRepository();
        var service = new AuthService(users);

        var employeeId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = employeeId,
                Login = "ivan",
                // BR34: хеш вместо открытого пароля; "secret" — сам пароль при сверке входа.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Ivan Petrov",
                Role = UserRole.Employee,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        // Act: BR35/BR36 apply to managers and administrators only, so five wrong
        // passwords for an employee must produce neither a lock nor a CAPTCHA.
        AuthResult fifthAttempt = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            fifthAttempt = await service.LoginAsync("ivan", "wrong password", ct);
        }

        // Assert
        Assert.False(fifthAttempt.IsSuccess);
        Assert.False(fifthAttempt.IsLocked);
        Assert.False(fifthAttempt.RequiresCaptcha);
        Assert.True(fifthAttempt.LockedUntil == null);

        var stored = await users.GetByLoginAsync("ivan", ct);
        Assert.NotNull(stored);
        Assert.Equal(5, stored.FailedLoginAttempts);
        Assert.False(stored.Locked);
        Assert.True(stored.LockedUntil == null);
    }

    [Fact]
    public async Task Login_SuccessfulLoginResetsFailuresAndLockState()
    {
        // Arrange
        var ct = CancellationToken.None;

        var users = new InMemoryUserRepository();
        var service = new AuthService(users);

        var managerId = Guid.NewGuid();
        await users.SaveAsync(
            new User
            {
                Id = managerId,
                Login = "manager",
                // BR34: хеш вместо открытого пароля; "secret" — сам пароль при сверке входа.
                PasswordHash = PasswordHasher.TryHash("secret"),
                FullName = "Maria Manager",
                Role = UserRole.Manager,
                DepartmentId = Guid.NewGuid(),
                IsActive = true,
            },
            ct);

        // Two wrong passwords first (the CAPTCHA countdown), then the right one.
        await service.LoginAsync("manager", "wrong password", ct);
        await service.LoginAsync("manager", "wrong password", ct);

        // Act
        var successful = await service.LoginAsync("manager", "secret", ct);

        // Assert: the failure counter and the lock state are cleared for the next session.
        Assert.True(successful.IsSuccess);
        Assert.False(successful.IsLocked);
        Assert.False(successful.RequiresCaptcha);

        var stored = await users.GetByLoginAsync("manager", ct);
        Assert.NotNull(stored);
        Assert.Equal(0, stored.FailedLoginAttempts);
        Assert.False(stored.Locked);
        Assert.True(stored.LockedUntil == null);
    }
}

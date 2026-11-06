// Domain-wide business constants used by services and validators.
// Members are compile-time-ish static values of the static class; callers write WorkTimeRules.MinDayDuration.

public static class WorkTimeRules
{
    public static readonly TimeSpan MinDayDuration = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan MinBreakDuration = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxBreakDuration = TimeSpan.FromHours(4);

    public const int MaxFailedAttempts = 10;

    public const int CaptchaThreshold = 2;
}

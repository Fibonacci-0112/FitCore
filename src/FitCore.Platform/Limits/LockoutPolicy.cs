namespace FitCore.Platform.Limits;

/// <summary>
/// When repeated failures lock a key out (NFR-SEC-4): <paramref name="Failures"/> within any
/// <paramref name="Window"/> start a lockout of <paramref name="FirstLockout"/>, and each further lockout within
/// <paramref name="Memory"/> of the first doubles, up to <paramref name="LongestLockout"/>.
/// </summary>
public sealed record LockoutPolicy(int Failures, TimeSpan Window, TimeSpan FirstLockout, TimeSpan LongestLockout, TimeSpan Memory)
{
    /// <summary>The <paramref name="lockouts"/>th lockout's length.</summary>
    public TimeSpan LockoutAfter(long lockouts)
    {
        var doublings = (int)Math.Clamp(lockouts - 1, 0, 16);
        var duration = FirstLockout * (1L << doublings);
        return duration < LongestLockout ? duration : LongestLockout;
    }
}

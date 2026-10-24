namespace FitCore.Platform.Limits;

/// <summary>
/// The counters behind the rate limits (NFR-SCL-5). Deployed, they live in Valkey, so every app instance sees the same
/// counts and the instances stay stateless (NFR-SCL-2, ADR 0003 decision 7); locally and in tests they live in memory.
/// Implementations never throw for an unreachable store: they answer as if nothing had been counted, so an outage
/// lets members in rather than locking everyone out. Supabase Auth's own per-IP limits still apply then.
/// </summary>
public interface ILimitStore
{
    /// <summary>
    /// Takes one token from the bucket at <paramref name="key"/>, which holds up to <paramref name="capacity"/> tokens,
    /// starts full and gains one every <paramref name="refillEvery"/>.
    /// </summary>
    /// <returns>Null when a token was taken; otherwise how long until the next one.</returns>
    Task<TimeSpan?> TakeTokenAsync(string key, int capacity, TimeSpan refillEvery);

    /// <summary>
    /// Records a failure at <paramref name="key"/> and, when it is the policy's last within its window, locks the key
    /// out and forgets the failures. One atomic step, so a concurrent <see cref="ClearFailuresAsync"/> lands wholly
    /// before or after it.
    /// </summary>
    /// <returns>How long the lockout lasts, when this failure started one; otherwise null.</returns>
    Task<TimeSpan?> RecordFailureAsync(string key, LockoutPolicy policy);

    /// <summary>How much longer <paramref name="key"/> is locked out; null when it isn't.</summary>
    Task<TimeSpan?> LockedForAsync(string key);

    /// <summary>Forgets <paramref name="key"/>'s failures, lockout history and any lockout, in one atomic step.</summary>
    Task ClearFailuresAsync(string key);
}

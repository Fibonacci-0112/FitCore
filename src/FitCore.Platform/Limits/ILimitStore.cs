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

    /// <summary>Adds one to the counter at <paramref name="key"/>, which disappears <paramref name="window"/> after its first count.</summary>
    /// <returns>The count after this one.</returns>
    Task<long> IncrementAsync(string key, TimeSpan window);

    /// <summary>Sets a flag at <paramref name="key"/> that lasts <paramref name="duration"/>, replacing any before it.</summary>
    Task SetAsync(string key, TimeSpan duration);

    /// <summary>How much longer the flag at <paramref name="key"/> lasts; null when it isn't set.</summary>
    Task<TimeSpan?> RemainingAsync(string key);

    Task DeleteAsync(params string[] keys);
}

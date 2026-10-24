using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace FitCore.Platform.Limits;

/// <summary>
/// The limits on fitcore-app's account endpoints (NFR-SCL-5, NFR-SEC-4, ADR 0005 decision 9), on top of Supabase
/// Auth's own per-IP limits:
/// <list type="bullet">
/// <item>Per address: 10 attempts a minute, with bursts up to 20, shared by sign-in and sign-up.</item>
/// <item>Per account: 5 failed sign-ins within any 15 minutes lock the email address out of sign-in for 15 minutes.
/// Each further lockout within a day of the first doubles, up to a day. A successful sign-in clears the record.</item>
/// </list>
/// A lockout applies to addresses with no account too, since Supabase Auth answers both alike, so it never shows
/// whether an account exists. Keys are hashes: neither email addresses nor IP addresses reach the store.
/// </summary>
public sealed class SignInLimits
{
    public const int AttemptsPerMinute = 10;

    /// <summary>Bursts up to twice the per-minute limit while the bucket has tokens (NFR-SCL-5).</summary>
    public const int Burst = 2 * AttemptsPerMinute;

    public const int FailuresBeforeLockout = 5;

    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan FirstLockout = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan LongestLockout = TimeSpan.FromDays(1);

    /// <summary>How long after an account's first lockout further lockouts keep doubling.</summary>
    public static readonly TimeSpan LockoutMemory = TimeSpan.FromDays(1);

    /// <summary>The per-account lockout, from the constants above.</summary>
    public static readonly LockoutPolicy Lockout = new(FailuresBeforeLockout, FailureWindow, FirstLockout, LongestLockout, LockoutMemory);

    private static readonly TimeSpan RefillEvery = TimeSpan.FromMinutes(1) / AttemptsPerMinute;

    private readonly ILimitStore _store;

    public SignInLimits(ILimitStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Counts an attempt from <paramref name="ip"/> at an account endpoint: sign-in, sign-up, and later password reset
    /// and step-up. Call it before asking Supabase Auth, so a refused attempt never reaches it.
    /// </summary>
    /// <returns>Null when the attempt may go ahead; otherwise how long to wait.</returns>
    public Task<TimeSpan?> CheckAttemptAsync(IPAddress? ip) =>
        _store.TakeTokenAsync("ip:" + Address(ip), Burst, RefillEvery);

    /// <summary>How much longer <paramref name="email"/> is locked out of sign-in; null when it isn't.</summary>
    public Task<TimeSpan?> LockedForAsync(string email) => _store.LockedForAsync(Account(email));

    /// <summary>Records a wrong email address or password, locking the address out at the fifth within the window.</summary>
    /// <returns>How long the lockout lasts, when this failure started one; otherwise null.</returns>
    public Task<TimeSpan?> RecordFailureAsync(string email) => _store.RecordFailureAsync(Account(email), Lockout);

    /// <summary>
    /// Clears an account's failures, lockout history and any lockout once its owner has signed in, including one that a
    /// wrong password racing this sign-in started.
    /// </summary>
    public Task RecordSuccessAsync(string email) => _store.ClearFailuresAsync(Account(email));

    /// <summary>The <paramref name="lockouts"/>th lockout's length: 15 minutes, doubling, at most a day.</summary>
    public static TimeSpan LockoutAfter(long lockouts) => Lockout.LockoutAfter(lockouts);

    // Supabase Auth treats addresses case-insensitively, so the count does too.
    private static string Account(string email) =>
        "account:" + Hash(email.Trim().ToLowerInvariant());

    // An IPv6 network hands each customer a /64, so one member's addresses share a bucket.
    private static string Address(IPAddress? ip)
    {
        if (ip is null)
        {
            return "unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            ip = new IPAddress(bytes);
        }

        return Hash(ip.ToString());
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

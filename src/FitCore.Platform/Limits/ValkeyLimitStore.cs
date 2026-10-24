using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FitCore.Platform.Limits;

/// <summary>
/// <see cref="ILimitStore"/> in Valkey, shared by every app instance (ADR 0003 decision 7, ADR 0005 decision 9). Each
/// operation is a single atomic command or script, and the token bucket reads Valkey's own clock, so instances with
/// skewed clocks still agree. Keys hold no personal data: callers pass hashes (<see cref="SignInLimits"/>).
/// </summary>
public sealed partial class ValkeyLimitStore : ILimitStore
{
    // The bucket's tokens refill continuously; a bucket idle long enough to be full again expires.
    private const string TakeTokenScript =
        """
        local capacity = tonumber(ARGV[1])
        local refill_ms = tonumber(ARGV[2])
        local clock = redis.call('TIME')
        local now = tonumber(clock[1]) * 1000 + math.floor(tonumber(clock[2]) / 1000)
        local state = redis.call('HMGET', KEYS[1], 'tokens', 'at')
        local tokens = tonumber(state[1])
        local at = tonumber(state[2])
        if tokens == nil or at == nil then
          tokens = capacity
          at = now
        end
        tokens = math.min(capacity, tokens + (now - at) / refill_ms)
        local wait = 0
        if tokens >= 1 then
          tokens = tokens - 1
        else
          wait = math.ceil((1 - tokens) * refill_ms)
        end
        redis.call('HSET', KEYS[1], 'tokens', tostring(tokens), 'at', tostring(now))
        redis.call('PEXPIRE', KEYS[1], math.ceil(capacity * refill_ms))
        return wait
        """;

    // KEYS: failures, lockouts, locked. The failures list keeps the times of the latest few, newest first; the oldest of
    // a full list falling inside the window means that many failed within it.
    private const string RecordFailureScript =
        """
        local failures = tonumber(ARGV[1])
        local window_ms = tonumber(ARGV[2])
        local clock = redis.call('TIME')
        local now = tonumber(clock[1]) * 1000 + math.floor(tonumber(clock[2]) / 1000)
        redis.call('LPUSH', KEYS[1], now)
        redis.call('LTRIM', KEYS[1], 0, failures - 1)
        redis.call('PEXPIRE', KEYS[1], window_ms)
        local oldest = tonumber(redis.call('LINDEX', KEYS[1], failures - 1))
        if oldest == nil or oldest <= now - window_ms then
          return 0
        end
        redis.call('DEL', KEYS[1])
        local lockouts = redis.call('INCR', KEYS[2])
        if lockouts == 1 then
          redis.call('PEXPIRE', KEYS[2], ARGV[5])
        end
        local duration = math.min(tonumber(ARGV[3]) * 2 ^ math.min(lockouts - 1, 16), tonumber(ARGV[4]))
        redis.call('SET', KEYS[3], 1, 'PX', duration)
        return duration
        """;

    private readonly IConnectionMultiplexer _valkey;
    private readonly string _prefix;
    private readonly ILogger<ValkeyLimitStore> _logger;

    /// <param name="valkey">The connection, shared by the host.</param>
    /// <param name="prefix">Put before every key, so FitCore's counters never collide with anything else in the database.</param>
    /// <param name="logger">Where an unreachable Valkey is reported; never with a key.</param>
    public ValkeyLimitStore(IConnectionMultiplexer valkey, string prefix, ILogger<ValkeyLimitStore> logger)
    {
        _valkey = valkey;
        _prefix = prefix;
        _logger = logger;
    }

    private IDatabase Database => _valkey.GetDatabase();

    public async Task<TimeSpan?> TakeTokenAsync(string key, int capacity, TimeSpan refillEvery)
    {
        var wait = await Try(
            async () => (long)await Database.ScriptEvaluateAsync(
                TakeTokenScript, [Key(key)], [capacity, Milliseconds(refillEvery)]),
            0L);
        return wait > 0 ? TimeSpan.FromMilliseconds(wait) : null;
    }

    public async Task<TimeSpan?> RecordFailureAsync(string key, LockoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var duration = await Try(
            async () => (long)await Database.ScriptEvaluateAsync(
                RecordFailureScript,
                [Key(key + ":failures"), Key(key + ":lockouts"), Key(key + ":locked")],
                [
                    policy.Failures,
                    Milliseconds(policy.Window),
                    Milliseconds(policy.FirstLockout),
                    Milliseconds(policy.LongestLockout),
                    Milliseconds(policy.Memory),
                ]),
            0L);
        return duration > 0 ? TimeSpan.FromMilliseconds(duration) : null;
    }

    public Task<TimeSpan?> LockedForAsync(string key) =>
        Try(() => Database.KeyTimeToLiveAsync(Key(key + ":locked")), null);

    public Task ClearFailuresAsync(string key) =>
        Try(() => Database.KeyDeleteAsync([Key(key + ":failures"), Key(key + ":lockouts"), Key(key + ":locked")]), 0L);

    private static long Milliseconds(TimeSpan span) => (long)Math.Ceiling(span.TotalMilliseconds);

    private RedisKey Key(string key) => _prefix + key;

    // An unreachable or failing Valkey counts nothing (ILimitStore): members can still sign in.
    private async Task<T> Try<T>(Func<Task<T>> operation, T unavailable)
    {
        try
        {
            return await operation();
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            LogUnavailable(_logger, e.GetType().Name);
            return unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate-limit counters are unavailable ({Error}); the request was let through.")]
    private static partial void LogUnavailable(ILogger logger, string error);
}

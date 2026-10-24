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

    private const string IncrementScript =
        """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
          redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return count
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
                TakeTokenScript, [Key(key)], [capacity, (long)Math.Ceiling(refillEvery.TotalMilliseconds)]),
            0L);
        return wait > 0 ? TimeSpan.FromMilliseconds(wait) : null;
    }

    public Task<long> IncrementAsync(string key, TimeSpan window) =>
        Try(
            async () => (long)await Database.ScriptEvaluateAsync(
                IncrementScript, [Key(key)], [(long)Math.Ceiling(window.TotalMilliseconds)]),
            0L);

    public Task SetAsync(string key, TimeSpan duration) =>
        Try(() => Database.StringSetAsync(Key(key), 1, duration), false);

    public Task<TimeSpan?> RemainingAsync(string key) =>
        Try(() => Database.KeyTimeToLiveAsync(Key(key)), null);

    public Task DeleteAsync(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Try(() => Database.KeyDeleteAsync([.. keys.Select(Key)]), 0L);
    }

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

namespace FitCore.Platform.Limits;

/// <summary>
/// <see cref="ILimitStore"/> in this process's memory: for local development and tests, where there is one instance.
/// Deployed hosts use <see cref="ValkeyLimitStore"/>, or each instance would allow the full limit on its own.
/// </summary>
public sealed class MemoryLimitStore : ILimitStore
{
    // Above this many entries, expired ones are swept on the next write, so a flood of addresses can't grow it forever.
    private const int SweepAbove = 10_000;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    public MemoryLimitStore(TimeProvider time)
    {
        _time = time;
    }

    public Task<TimeSpan?> TakeTokenAsync(string key, int capacity, TimeSpan refillEvery)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var tokens = Live(key, now) is { } bucket
                ? Math.Min(capacity, bucket.Tokens + ((now - bucket.At) / refillEvery))
                : capacity;

            TimeSpan? wait = null;
            if (tokens >= 1)
            {
                tokens--;
            }
            else
            {
                wait = (1 - tokens) * refillEvery;
            }

            // A bucket left alone that long is full again, the same as no bucket.
            Put(key, new Entry(tokens, now, now + (capacity * refillEvery)), now);
            return Task.FromResult(wait);
        }
    }

    public Task<long> IncrementAsync(string key, TimeSpan window)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var entry = Live(key, now) is { } counter
                ? counter with { Tokens = counter.Tokens + 1 }
                : new Entry(1, now, now + window);
            Put(key, entry, now);
            return Task.FromResult((long)entry.Tokens);
        }
    }

    public Task SetAsync(string key, TimeSpan duration)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Put(key, new Entry(1, now, now + duration), now);
            return Task.CompletedTask;
        }
    }

    public Task<TimeSpan?> RemainingAsync(string key)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return Task.FromResult(Live(key, now) is { } flag ? flag.Expires - now : (TimeSpan?)null);
        }
    }

    public Task DeleteAsync(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        lock (_gate)
        {
            foreach (var key in keys)
            {
                _entries.Remove(key);
            }

            return Task.CompletedTask;
        }
    }

    // Holds the gate.
    private Entry? Live(string key, DateTimeOffset now)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.Expires > now)
        {
            return entry;
        }

        _entries.Remove(key);
        return null;
    }

    // Holds the gate.
    private void Put(string key, Entry entry, DateTimeOffset now)
    {
        if (_entries.Count > SweepAbove)
        {
            foreach (var expired in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList())
            {
                _entries.Remove(expired);
            }
        }

        _entries[key] = entry;
    }

    /// <summary>A bucket's tokens, a counter's count or a flag, with when it was last updated and when it goes.</summary>
    private sealed record Entry(double Tokens, DateTimeOffset At, DateTimeOffset Expires);
}

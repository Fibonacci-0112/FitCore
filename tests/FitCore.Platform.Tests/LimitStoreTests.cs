using System.Net;
using FitCore.Platform.Limits;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace FitCore.Platform.Tests;

/// <summary>
/// The rate-limit counters and the sign-in policy over them (NFR-SCL-5, NFR-SEC-4), against both stores: memory, as in
/// local development and tests, and a real Valkey, as deployed. Override Valkey's address with FITCORE_TEST_VALKEY.
/// </summary>
public sealed class LimitStoreTests : IAsyncLifetime
{
    private static readonly string ValkeyAddress = Environment.GetEnvironmentVariable("FITCORE_TEST_VALKEY") ?? "127.0.0.1:6379";

    private readonly SettableTime _time = new();
    private ConnectionMultiplexer? _valkey;

    public static TheoryData<string> Stores => ["memory", "valkey"];

    public async ValueTask InitializeAsync() => _valkey = await ConnectionMultiplexer.ConnectAsync(ValkeyAddress);

    public async ValueTask DisposeAsync()
    {
        if (_valkey is not null)
        {
            await _valkey.DisposeAsync();
        }
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_bucket_gives_its_capacity_then_says_when_the_next_token_comes(string kind)
    {
        var store = Store(kind);

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await store.TakeTokenAsync("bucket", 3, TimeSpan.FromSeconds(10)));
        }

        var wait = Assert.IsType<TimeSpan>(await store.TakeTokenAsync("bucket", 3, TimeSpan.FromSeconds(10)));
        Assert.InRange(wait, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_bucket_refills_one_token_per_interval()
    {
        var store = new MemoryLimitStore(_time);
        for (var i = 0; i < 3; i++)
        {
            await store.TakeTokenAsync("bucket", 3, TimeSpan.FromSeconds(10));
        }

        _time.Now += TimeSpan.FromSeconds(10);

        Assert.Null(await store.TakeTokenAsync("bucket", 3, TimeSpan.FromSeconds(10)));
        Assert.NotNull(await store.TakeTokenAsync("bucket", 3, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_counter_counts_and_a_flag_says_how_long_it_has_left(string kind)
    {
        var store = Store(kind);

        Assert.Equal(1, await store.IncrementAsync("counter", TimeSpan.FromMinutes(15)));
        Assert.Equal(2, await store.IncrementAsync("counter", TimeSpan.FromMinutes(15)));
        Assert.Null(await store.RemainingAsync("flag"));

        await store.SetAsync("flag", TimeSpan.FromMinutes(30));
        var left = Assert.IsType<TimeSpan>(await store.RemainingAsync("flag"));
        Assert.InRange(left, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));

        await store.DeleteAsync("counter", "flag");
        Assert.Null(await store.RemainingAsync("flag"));
        Assert.Equal(1, await store.IncrementAsync("counter", TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task A_counter_starts_again_once_its_window_has_passed()
    {
        var store = new MemoryLimitStore(_time);
        await store.IncrementAsync("counter", TimeSpan.FromMinutes(15));

        _time.Now += TimeSpan.FromMinutes(15);

        Assert.Equal(1, await store.IncrementAsync("counter", TimeSpan.FromMinutes(15)));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task The_fifth_failure_locks_the_address_and_each_further_lockout_doubles(string kind)
    {
        var limits = new SignInLimits(Store(kind));

        Assert.Equal([null, null, null, null, SignInLimits.FirstLockout], await FailAsync(limits, 5));
        Assert.InRange((await limits.LockedForAsync("ada@example.test")).GetValueOrDefault(), TimeSpan.FromMinutes(14), SignInLimits.FirstLockout);
        Assert.Equal(2 * SignInLimits.FirstLockout, (await FailAsync(limits, 5))[^1]);
        Assert.Equal(4 * SignInLimits.FirstLockout, (await FailAsync(limits, 5))[^1]);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Signing_in_clears_the_failures_and_the_lockout_history(string kind)
    {
        var limits = new SignInLimits(Store(kind));
        await FailAsync(limits, 5);
        await FailAsync(limits, 4);

        await limits.RecordSuccessAsync("ada@example.test");

        Assert.Equal([null, null, null, null, SignInLimits.FirstLockout], await FailAsync(limits, 5));
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(6, 480)]
    [InlineData(7, 960)]
    [InlineData(8, 1440)]
    [InlineData(1000, 1440)]
    public void Lockouts_double_from_15_minutes_up_to_a_day(long lockouts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SignInLimits.LockoutAfter(lockouts));
    }

    [Fact]
    public async Task One_IPv6_network_shares_a_bucket_and_a_mapped_IPv4_address_is_the_IPv4_one()
    {
        var limits = new SignInLimits(new MemoryLimitStore(_time));
        for (var i = 0; i < SignInLimits.Burst; i++)
        {
            Assert.Null(await limits.CheckAttemptAsync(IPAddress.Parse("2001:db8:0:1::" + (i + 1).ToString("x", System.Globalization.CultureInfo.InvariantCulture))));
            Assert.Null(await limits.CheckAttemptAsync(IPAddress.Parse("192.0.2.10")));
        }

        Assert.NotNull(await limits.CheckAttemptAsync(IPAddress.Parse("2001:db8:0:1::ffff")));
        Assert.NotNull(await limits.CheckAttemptAsync(IPAddress.Parse("::ffff:192.0.2.10")));
        Assert.Null(await limits.CheckAttemptAsync(IPAddress.Parse("2001:db8:0:2::1")));
    }

    [Fact]
    public async Task Unreachable_Valkey_lets_every_attempt_through()
    {
        // Nothing listens on the discard port.
        await using var nowhere = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:9" },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            AsyncTimeout = 200,
            SyncTimeout = 200,
        });
        var limits = new SignInLimits(new ValkeyLimitStore(nowhere, "fitcore:test:", NullLogger<ValkeyLimitStore>.Instance));

        Assert.Null(await limits.CheckAttemptAsync(IPAddress.Loopback));
        Assert.Null(await limits.RecordFailureAsync("ada@example.test"));
        Assert.Null(await limits.LockedForAsync("ada@example.test"));
    }

    private static async Task<TimeSpan?[]> FailAsync(SignInLimits limits, int times)
    {
        var lockouts = new TimeSpan?[times];
        for (var i = 0; i < times; i++)
        {
            lockouts[i] = await limits.RecordFailureAsync("ada@example.test");
        }

        return lockouts;
    }

    // Each test gets its own Valkey keys, so runs don't share counters.
    private ILimitStore Store(string kind) => kind == "memory"
        ? new MemoryLimitStore(_time)
        : new ValkeyLimitStore(_valkey!, $"fitcore:test:{Guid.NewGuid():N}:", NullLogger<ValkeyLimitStore>.Instance);

    private sealed class SettableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

using FitCore.Client.Core.Accounts;

namespace FitCore.Client.Tests;

/// <summary>
/// The member's session on a device: kept in the secure store across launches, refreshed before it expires, kept
/// when Auth is out of reach, and gone from the device only once Supabase Auth has ended it (PR 4 acceptance).
/// </summary>
public sealed class DeviceSessionTests : IDisposable
{
    private readonly ScriptedAccounts _accounts = new();
    private readonly MemoryStore _store = new();
    private readonly SettableTime _time = new();
    private readonly DeviceSession _session;
    private int _changes;

    public DeviceSessionTests()
    {
        _session = new DeviceSession(_accounts, _store, _time);
        _session.SignedInChanged += (_, _) => _changes++;
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _session.Dispose();

    [Fact]
    public async Task Signing_in_keeps_the_session_in_the_store_and_says_so()
    {
        var status = await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);

        Assert.Equal(SignInStatus.SignedIn, status);
        Assert.True(_session.IsSignedIn);
        Assert.Equal(SessionFakes.Session("first"), _store.Saved);
        Assert.Equal(1, _changes);
    }

    [Theory]
    [InlineData(SignInStatus.InvalidCredentials)]
    [InlineData(SignInStatus.EmailNotConfirmed)]
    [InlineData(SignInStatus.RateLimited)]
    [InlineData(SignInStatus.Unavailable)]
    public async Task A_failed_sign_in_keeps_nothing(SignInStatus failure)
    {
        _accounts.SignIn = new(failure);

        Assert.Equal(failure, await _session.SignInAsync("ada@example.test", "wrong", Cancel));

        Assert.False(_session.IsSignedIn);
        Assert.Null(_store.Saved);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public async Task A_saved_session_is_restored_without_a_call_while_its_token_is_fresh()
    {
        _store.Saved = SessionFakes.Session("saved");

        Assert.True(await _session.RestoreAsync(Cancel));

        Assert.Empty(_accounts.Refreshes);
        Assert.Equal("access-saved", await _session.GetAccessTokenAsync(Cancel));
        Assert.Equal(0, _changes);
    }

    [Fact]
    public async Task Nothing_saved_means_signed_out()
    {
        Assert.False(await _session.RestoreAsync(Cancel));
        Assert.Null(await _session.GetAccessTokenAsync(Cancel));
    }

    [Fact]
    public async Task A_saved_session_that_expired_is_refreshed_at_launch_and_the_new_one_saved()
    {
        _store.Saved = SessionFakes.Session("saved", TimeSpan.FromMinutes(-5));

        Assert.True(await _session.RestoreAsync(Cancel));

        Assert.Equal(SessionFakes.Session("saved", TimeSpan.FromMinutes(-5)), Assert.Single(_accounts.Refreshes));
        Assert.Equal(SessionFakes.Session("refreshed"), _store.Saved);
    }

    [Fact]
    public async Task A_saved_session_Auth_has_ended_is_deleted_at_launch()
    {
        _store.Saved = SessionFakes.Session("saved", TimeSpan.FromMinutes(-5));
        _accounts.Refresh = new(RefreshStatus.SessionEnded);

        Assert.False(await _session.RestoreAsync(Cancel));

        Assert.Null(_store.Saved);
    }

    [Fact]
    public async Task Offline_at_launch_keeps_the_saved_session_but_hands_out_no_expired_token()
    {
        _store.Saved = SessionFakes.Session("saved", TimeSpan.FromMinutes(-5));
        _accounts.Refresh = new(RefreshStatus.Unavailable);

        Assert.True(await _session.RestoreAsync(Cancel));

        Assert.NotNull(_store.Saved);
        Assert.Null(await _session.GetAccessTokenAsync(Cancel));
        Assert.True(_session.IsSignedIn);
    }

    [Fact]
    public async Task A_token_within_a_minute_of_expiring_is_refreshed_before_it_is_handed_out()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _time.Now += TimeSpan.FromMinutes(14.5);

        Assert.Equal("access-refreshed", await _session.GetAccessTokenAsync(Cancel));
        Assert.Equal(SessionFakes.Session("refreshed"), _store.Saved);
    }

    [Fact]
    public async Task A_token_that_is_still_fresh_is_handed_out_without_a_call()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _time.Now += TimeSpan.FromMinutes(13);

        Assert.Equal("access-first", await _session.GetAccessTokenAsync(Cancel));
        Assert.Empty(_accounts.Refreshes);
    }

    [Fact]
    public async Task Offline_near_expiry_still_hands_out_the_token_until_it_expires()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _accounts.Refresh = new(RefreshStatus.Unavailable);
        _time.Now += TimeSpan.FromMinutes(14.5);

        Assert.Equal("access-first", await _session.GetAccessTokenAsync(Cancel));
    }

    [Fact]
    public async Task A_refresh_Auth_refuses_signs_the_member_out_and_says_so()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _accounts.Refresh = new(RefreshStatus.SessionEnded);
        _time.Now += TimeSpan.FromMinutes(20);

        Assert.Null(await _session.GetAccessTokenAsync(Cancel));

        Assert.False(_session.IsSignedIn);
        Assert.Null(_store.Saved);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public async Task Parallel_callers_near_expiry_share_one_refresh()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _accounts.Refresh = new(RefreshStatus.Refreshed, SessionFakes.Session("refreshed", TimeSpan.FromHours(1)));
        _time.Now += TimeSpan.FromMinutes(14.5);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _session.GetAccessTokenAsync(Cancel)));

        Assert.All(tokens, token => Assert.Equal("access-refreshed", token));
        Assert.Single(_accounts.Refreshes);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_with_Auth_then_deletes_it_from_the_device()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);

        Assert.Equal(SignOutStatus.SessionEnded, await _session.SignOutAsync(Cancel));

        Assert.Equal(SessionFakes.Session("first"), Assert.Single(_accounts.SignOuts));
        Assert.False(_session.IsSignedIn);
        Assert.Null(_store.Saved);
        Assert.Null(await _session.GetAccessTokenAsync(Cancel));
        Assert.Equal(2, _changes);
    }

    [Fact]
    public async Task A_sign_out_Auth_cant_confirm_keeps_the_member_signed_in_to_try_again()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _accounts.SignOut = SignOutStatus.Unavailable;

        Assert.Equal(SignOutStatus.Unavailable, await _session.SignOutAsync(Cancel));

        Assert.True(_session.IsSignedIn);
        Assert.NotNull(_store.Saved);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public async Task Signing_out_with_an_expired_token_refreshes_first_so_logout_is_accepted()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _time.Now += TimeSpan.FromMinutes(20);

        await _session.SignOutAsync(Cancel);

        Assert.Equal(SessionFakes.Session("refreshed"), Assert.Single(_accounts.SignOuts));
        Assert.Null(_store.Saved);
    }

    [Fact]
    public async Task Signing_out_a_session_Auth_already_ended_needs_no_logout()
    {
        await _session.SignInAsync("ada@example.test", "correct horse battery staple", Cancel);
        _accounts.Refresh = new(RefreshStatus.SessionEnded);
        _time.Now += TimeSpan.FromMinutes(20);

        Assert.Equal(SignOutStatus.SessionEnded, await _session.SignOutAsync(Cancel));

        Assert.Empty(_accounts.SignOuts);
        Assert.False(_session.IsSignedIn);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void A_session_never_prints_its_tokens()
    {
        var text = SessionFakes.Session("secret").ToString();

        Assert.DoesNotContain("access-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", text, StringComparison.Ordinal);
    }
}

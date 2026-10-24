namespace FitCore.Client.Core.Accounts;

/// <summary>
/// The member's session on this device (implementation plan §3.3): signed in with <see cref="IMemberAccounts"/>, kept
/// in the secure store so it survives the app being closed, and refreshed before its access token expires. One
/// instance per app. Anything that caches member data clears it when <see cref="SignedInChanged"/> reports a
/// sign-out, so nothing of the member's stays on the device (PR 4 acceptance).
/// </summary>
public sealed class DeviceSession : IDisposable
{
    /// <summary>How long before the access token expires that it is refreshed, so no call starts with a token about to lapse.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly IMemberAccounts _accounts;
    private readonly ISessionStore _store;
    private readonly TimeProvider _time;

    // One change at a time: refresh tokens are single-use, so two refreshes racing would sign the member out.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MemberSession? _session;

    public DeviceSession(IMemberAccounts accounts, ISessionStore store, TimeProvider time)
    {
        _accounts = accounts;
        _store = store;
        _time = time;
    }

    /// <summary>Raised after the member signs in or the session ends, from whichever thread made the change.</summary>
    public event EventHandler? SignedInChanged;

    public bool IsSignedIn => _session is not null;

    /// <summary>
    /// Picks up the session saved by an earlier launch. A session Supabase Auth has ended is deleted; one that can't
    /// be refreshed only because Auth is out of reach is kept, so the app still opens offline.
    /// </summary>
    /// <returns>Whether the member is signed in. Raises no <see cref="SignedInChanged"/>: the app starts from this answer.</returns>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _session = await _store.LoadAsync();
            if (_session is not null && NeedsRefresh(_session))
            {
                await RefreshAsync(cancellationToken);
            }

            return _session is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Signs the member in, and keeps the session for later launches.</summary>
    public async Task<SignInStatus> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var result = await _accounts.SignInAsync(email, password, cancellationToken);
        if (result.Status != SignInStatus.SignedIn)
        {
            return result.Status;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _session = result.Session ?? throw new InvalidOperationException("A sign-in that worked came back without a session.");
            await _store.SaveAsync(_session);
        }
        finally
        {
            _gate.Release();
        }

        SignedInChanged?.Invoke(this, EventArgs.Empty);
        return SignInStatus.SignedIn;
    }

    /// <summary>
    /// An access token for the API, refreshed first if it is about to expire. Null when the member isn't signed in,
    /// the session has ended, or the token has expired and Auth can't be reached to refresh it.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var wasSignedIn = IsSignedIn;
        string? token;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && NeedsRefresh(_session))
            {
                await RefreshAsync(cancellationToken);
            }

            token = _session is { } session && session.ExpiresAt > _time.GetUtcNow() ? session.AccessToken : null;
        }
        finally
        {
            _gate.Release();
        }

        if (wasSignedIn && !IsSignedIn)
        {
            SignedInChanged?.Invoke(this, EventArgs.Empty);
        }

        return token;
    }

    /// <summary>
    /// Ends the session with Supabase Auth, which revokes its refresh tokens, then deletes it from the device. If Auth
    /// can't be reached, the member stays signed in and can try again, as on the web (ADR 0005 decision 7): deleting
    /// only the device's copy would leave a session that someone holding its refresh token could still use.
    /// </summary>
    public async Task<SignOutStatus> SignOutAsync(CancellationToken cancellationToken = default)
    {
        var wasSignedIn = IsSignedIn;
        var status = SignOutStatus.SessionEnded;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Logout needs an access token that hasn't expired.
            if (_session is not null && NeedsRefresh(_session))
            {
                await RefreshAsync(cancellationToken);
            }

            if (_session is not null)
            {
                status = await _accounts.SignOutAsync(_session, cancellationToken);
                if (status == SignOutStatus.SessionEnded)
                {
                    await EndAsync();
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (wasSignedIn && !IsSignedIn)
        {
            SignedInChanged?.Invoke(this, EventArgs.Empty);
        }

        return status;
    }

    public void Dispose() => _gate.Dispose();

    private bool NeedsRefresh(MemberSession session) => session.ExpiresAt - _time.GetUtcNow() < RefreshMargin;

    // Holds the gate.
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _accounts.RefreshAsync(_session!, cancellationToken);
        switch (result.Status)
        {
            case RefreshStatus.Refreshed:
                _session = result.Session ?? throw new InvalidOperationException("A refresh that worked came back without a session.");
                await _store.SaveAsync(_session);
                break;
            case RefreshStatus.SessionEnded:
                await EndAsync();
                break;
            default:
                // Out of reach: keep the session and try again on the next call.
                break;
        }
    }

    // Holds the gate.
    private async Task EndAsync()
    {
        _session = null;
        await _store.ClearAsync();
    }
}

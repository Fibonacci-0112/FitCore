using FitCore.Client.Core.Accounts;

namespace FitCore.Client.Tests;

/// <summary>Supabase Auth as <see cref="DeviceSession"/> sees it, answering with whatever a test sets.</summary>
internal sealed class ScriptedAccounts : IMemberAccounts
{
    public SignInResult SignIn { get; set; } = new(SignInStatus.SignedIn, SessionFakes.Session("first"));

    public RefreshResult Refresh { get; set; } = new(RefreshStatus.Refreshed, SessionFakes.Session("refreshed"));

    public SignOutStatus SignOut { get; set; } = SignOutStatus.SessionEnded;

    /// <summary>When set, a refresh waits for this to finish before it answers, so a test can hold one in flight.</summary>
    public Task? RefreshHeld { get; set; }

    /// <summary>The cancellation token each refresh was given.</summary>
    public List<CancellationToken> RefreshTokens { get; } = [];

    /// <summary>Read while each call runs, such as a view model's busy flag.</summary>
    public Func<bool> Observe { get; set; } = () => false;

    public List<bool> BusyDuringCalls { get; } = [];

    public List<(string Email, string Password)> SignIns { get; } = [];

    public List<MemberSession> Refreshes { get; } = [];

    public List<MemberSession> SignOuts { get; } = [];

    public Task<SignUpStatus> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<SignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        SignIns.Add((email, password));
        BusyDuringCalls.Add(Observe());
        return Task.FromResult(SignIn);
    }

    public async Task<RefreshResult> RefreshAsync(MemberSession session, CancellationToken cancellationToken = default)
    {
        Refreshes.Add(session);
        RefreshTokens.Add(cancellationToken);
        if (RefreshHeld is not null)
        {
            await RefreshHeld;
        }

        return Refresh;
    }

    public Task<SignOutStatus> SignOutAsync(MemberSession session, CancellationToken cancellationToken = default)
    {
        SignOuts.Add(session);
        BusyDuringCalls.Add(Observe());
        return Task.FromResult(SignOut);
    }
}

/// <summary>The secure store, in memory.</summary>
internal sealed class MemoryStore : ISessionStore
{
    public MemberSession? Saved { get; set; }

    public Task<MemberSession?> LoadAsync() => Task.FromResult(Saved);

    public Task SaveAsync(MemberSession session)
    {
        Saved = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Saved = null;
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class SettableTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = SessionFakes.Now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class SessionFakes
{
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A session named by its tokens, valid for 15 minutes from <see cref="Now"/> unless told otherwise.</summary>
    public static MemberSession Session(string name, TimeSpan? lifetime = null) =>
        new($"access-{name}", $"refresh-{name}", Now + (lifetime ?? TimeSpan.FromMinutes(15)));
}

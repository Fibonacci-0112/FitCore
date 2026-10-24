namespace FitCore.Client.Core.Accounts;

/// <summary>
/// Where a device keeps the member's session between launches: the platform secure store, never plain preferences or
/// files (NFR-SEC-8). Implementations never throw: a store that can't be read holds no session, and a write that
/// fails leaves the session in memory only, so the member signs in again on the next launch.
/// </summary>
public interface ISessionStore
{
    /// <summary>The saved session, or null when there is none or it can't be read.</summary>
    Task<MemberSession?> LoadAsync();

    Task SaveAsync(MemberSession session);

    Task ClearAsync();
}

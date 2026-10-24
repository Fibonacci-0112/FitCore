using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using FitCore.Client.Core.Accounts;

namespace FitCore.Maui.Accounts;

/// <summary>
/// The member's session in the platform secure store (NFR-SEC-8): the Keychain on iOS and macOS, Keystore-encrypted
/// preferences on Android and DPAPI on Windows. Android backup is off (AndroidManifest.xml), so an encrypted copy never
/// lands on a device whose key can't read it.
/// </summary>
internal sealed class SecureSessionStore : ISessionStore
{
    private const string SessionKey = "fitcore.session";

    // In plain preferences, which go when the app is uninstalled on every platform. The iOS Keychain doesn't.
    private const string InstalledKey = "fitcore.installed";

    private readonly ISecureStorage _secure;
    private readonly IPreferences _preferences;

    public SecureSessionStore(ISecureStorage secure, IPreferences preferences)
    {
        _secure = secure;
        _preferences = preferences;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Each platform's store fails in its own way; any failure means there is no session to use.")]
    public async Task<MemberSession?> LoadAsync()
    {
        try
        {
            // A session left in the Keychain by an earlier install isn't picked up: a reinstalled app starts signed
            // out, as it does on the other platforms.
            if (!_preferences.ContainsKey(InstalledKey))
            {
                _secure.Remove(SessionKey);
                _preferences.Set(InstalledKey, true);
                return null;
            }

            return await _secure.GetAsync(SessionKey) is { } json
                ? JsonSerializer.Deserialize(json, SessionJson.Default.MemberSession)
                : null;
        }
        catch (Exception)
        {
            // Unreadable, for example after the platform key changed: start signed out, and don't trip on it again.
            await ClearAsync();
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A session that can't be saved lives in memory only, and the member signs in again next launch.")]
    public async Task SaveAsync(MemberSession session)
    {
        try
        {
            await _secure.SetAsync(SessionKey, JsonSerializer.Serialize(session, SessionJson.Default.MemberSession));
        }
        catch (Exception)
        {
            await ClearAsync();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Nothing more can be done about a store that can't remove an entry.")]
    public Task ClearAsync()
    {
        try
        {
            _secure.Remove(SessionKey);
        }
        catch (Exception)
        {
        }

        return Task.CompletedTask;
    }
}

/// <summary>Source-generated, so the session's JSON survives trimming on iOS and Android.</summary>
[JsonSerializable(typeof(MemberSession))]
internal sealed partial class SessionJson : JsonSerializerContext;

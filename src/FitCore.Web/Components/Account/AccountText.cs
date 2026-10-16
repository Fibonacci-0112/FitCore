using FitCore.Client.Core.Resources;
using FitCore.Platform.Auth;

namespace FitCore.Web.Components.Account;

/// <summary>What the account pages tell the member about a failed attempt, from <see cref="AccountStrings"/> (NFR-L10N-2).</summary>
internal static class AccountText
{
    public static string For(AuthStatus status) => status switch
    {
        AuthStatus.InvalidCredentials => AccountStrings.InvalidCredentials,
        AuthStatus.EmailNotConfirmed => AccountStrings.EmailNotConfirmed,
        AuthStatus.EmailTaken => AccountStrings.EmailTaken,
        AuthStatus.WeakPassword => AccountStrings.WeakPassword,
        AuthStatus.Underage => AccountStrings.TooYoung,
        AuthStatus.InvalidLink => AccountStrings.InvalidLink,
        AuthStatus.Refused => AccountStrings.Refused,
        AuthStatus.RateLimited => AccountStrings.RateLimited,
        _ => AccountStrings.Unavailable,
    };

    /// <summary>Only paths on this site, so a sign-in link can't send the member somewhere else afterwards.</summary>
    public static string LocalOrHome(string? returnUrl) =>
        returnUrl is { Length: > 1 } url && url[0] == '/' && url[1] != '/' && url[1] != '\\' ? url : "/";
}

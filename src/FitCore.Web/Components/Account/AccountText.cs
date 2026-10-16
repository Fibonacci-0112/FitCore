using FitCore.Platform.Auth;

namespace FitCore.Web.Components.Account;

/// <summary>What the account pages tell the member about a failed attempt. No health data, and nothing that confirms whether someone else's account exists.</summary>
internal static class AccountText
{
    public const string TooYoung = "You need to be 18 or older to use FitCore.";

    public static string For(AuthStatus status) => status switch
    {
        AuthStatus.InvalidCredentials => "That email address and password don't match an account.",
        AuthStatus.EmailNotConfirmed => "Confirm your email address first: open the link we sent you. If it has expired, sign up again with the same address and we'll send a new one.",
        AuthStatus.EmailTaken => "An account with that email address already exists. Sign in instead.",
        AuthStatus.WeakPassword => "Choose a longer password, or one that hasn't appeared in a data breach.",
        AuthStatus.Underage => TooYoung,
        AuthStatus.InvalidLink => "That confirmation link has expired or was already used. If you haven't confirmed yet, sign up again with the same address and we'll send a new one.",
        AuthStatus.Refused => "FitCore couldn't use those details. Check your email address and try again.",
        AuthStatus.RateLimited => "Too many attempts. Wait a few minutes and try again.",
        _ => "FitCore couldn't reach its sign-in service. Try again in a moment.",
    };

    /// <summary>Only paths on this site, so a sign-in link can't send the member somewhere else afterwards.</summary>
    public static string LocalOrHome(string? returnUrl) =>
        returnUrl is { Length: > 1 } url && url[0] == '/' && url[1] != '/' && url[1] != '\\' ? url : "/";
}

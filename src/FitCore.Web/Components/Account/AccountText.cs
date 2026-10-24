using System.Globalization;
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

    /// <summary>
    /// Answers a refused attempt with 429 and <c>Retry-After</c> in whole seconds (NFR-SCL-5), and returns what the
    /// page tells the member. Only for a page that hasn't started its response, as a form post's hasn't.
    /// </summary>
    public static string Refuse(HttpContext context, TimeSpan wait, string message)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = ((long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return message;
    }

    /// <summary>Only paths on this site, so a sign-in link can't send the member somewhere else afterwards.</summary>
    public static string LocalOrHome(string? returnUrl) =>
        returnUrl is { Length: > 1 } url && url[0] == '/' && url[1] != '/' && url[1] != '\\' ? url : "/";
}

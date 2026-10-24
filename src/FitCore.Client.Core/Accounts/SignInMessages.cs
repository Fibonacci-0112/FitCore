using FitCore.Client.Core.Resources;

namespace FitCore.Client.Core.Accounts;

/// <summary>What the member is told when a sign-in fails: the web's messages, from <see cref="AccountStrings"/> (NFR-L10N-2).</summary>
public static class SignInMessages
{
    public static string For(SignInStatus status) => status switch
    {
        SignInStatus.InvalidCredentials => AccountStrings.InvalidCredentials,
        SignInStatus.EmailNotConfirmed => AccountStrings.EmailNotConfirmed,
        SignInStatus.RateLimited => AccountStrings.RateLimited,
        _ => AccountStrings.Unavailable,
    };
}

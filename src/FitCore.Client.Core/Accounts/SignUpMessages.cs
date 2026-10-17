using FitCore.Client.Core.Resources;

namespace FitCore.Client.Core.Accounts;

/// <summary>What the member is told when a sign-up fails, from <see cref="AccountStrings"/> (NFR-L10N-2).</summary>
public static class SignUpMessages
{
    /// <summary>Nothing that confirms someone else's account exists beyond what Supabase Auth itself says.</summary>
    public static string For(SignUpStatus status) => status switch
    {
        SignUpStatus.EmailTaken => AccountStrings.EmailTaken,
        SignUpStatus.WeakPassword => AccountStrings.WeakPassword,
        SignUpStatus.Underage => AccountStrings.TooYoung,
        SignUpStatus.RateLimited => AccountStrings.RateLimited,
        SignUpStatus.Refused => AccountStrings.Refused,
        _ => AccountStrings.Unavailable,
    };
}

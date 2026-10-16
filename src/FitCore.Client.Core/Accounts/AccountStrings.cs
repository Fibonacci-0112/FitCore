using System.Globalization;
using System.Resources;

namespace FitCore.Client.Core.Accounts;

/// <summary>
/// The account screens' text, from <c>AccountStrings.resx</c> in the current UI culture (NFR-L10N-2). XAML binds to these
/// with <c>x:Static</c>; view models use them for messages.
/// </summary>
public static class AccountStrings
{
    private static readonly ResourceManager Resources = new(typeof(AccountStrings).FullName!, typeof(AccountStrings).Assembly);

    public static string SignUpTitle => Get(nameof(SignUpTitle));

    public static string NameLabel => Get(nameof(NameLabel));

    public static string EmailLabel => Get(nameof(EmailLabel));

    public static string PasswordLabel => Get(nameof(PasswordLabel));

    public static string PasswordHelp => Get(nameof(PasswordHelp));

    public static string BirthDateLabel => Get(nameof(BirthDateLabel));

    public static string BirthDateHelp => Get(nameof(BirthDateHelp));

    public static string CreateAccount => Get(nameof(CreateAccount));

    public static string CreatingAccount => Get(nameof(CreatingAccount));

    public static string CheckEmailTitle => Get(nameof(CheckEmailTitle));

    public static string CheckEmailBody => Get(nameof(CheckEmailBody));

    public static string CheckEmailHelp => Get(nameof(CheckEmailHelp));

    public static string SignUpAgain => Get(nameof(SignUpAgain));

    public static string NameRequired => Get(nameof(NameRequired));

    public static string NameTooLong => Get(nameof(NameTooLong));

    public static string EmailRequired => Get(nameof(EmailRequired));

    public static string EmailInvalid => Get(nameof(EmailInvalid));

    public static string PasswordRequired => Get(nameof(PasswordRequired));

    public static string PasswordLength => Get(nameof(PasswordLength));

    public static string BirthDateRequired => Get(nameof(BirthDateRequired));

    public static string TooYoung => Get(nameof(TooYoung));

    public static string EmailTaken => Get(nameof(EmailTaken));

    public static string WeakPassword => Get(nameof(WeakPassword));

    public static string RateLimited => Get(nameof(RateLimited));

    public static string Refused => Get(nameof(Refused));

    public static string Unavailable => Get(nameof(Unavailable));

    /// <summary>What the member is told when a sign-up fails. Nothing that confirms someone else's account exists beyond what Supabase Auth itself says.</summary>
    public static string For(SignUpStatus status) => status switch
    {
        SignUpStatus.EmailTaken => EmailTaken,
        SignUpStatus.WeakPassword => WeakPassword,
        SignUpStatus.Underage => TooYoung,
        SignUpStatus.RateLimited => RateLimited,
        SignUpStatus.Refused => Refused,
        _ => Unavailable,
    };

    private static string Get(string name) =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? throw new MissingManifestResourceException($"AccountStrings has no {name}.");
}

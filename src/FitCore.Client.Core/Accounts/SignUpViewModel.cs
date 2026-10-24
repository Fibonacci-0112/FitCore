using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitCore.Client.Core.Resources;
using FitCore.Domain.Calendar;

namespace FitCore.Client.Core.Accounts;

/// <summary>
/// Email sign-up on the device apps (implementation plan PR 4), with the same rules and messages as the web's form.
/// The 18+ check here is for a clear message; the database refuses an under-18 birth date whatever the app sends. The
/// form is cleared whenever someone signs in or out, so the next person on the device sees nothing of the last one's
/// (PR 4 acceptance).
/// </summary>
public sealed partial class SignUpViewModel : ObservableObject
{
    private const int MaxNameLength = 80;
    private const int MaxEmailLength = 254;

    // NFR-SEC-2: at least 15 characters as the only factor, up to 64 allowed, no composition rules.
    private const int MinPasswordLength = 15;
    private const int MaxPasswordLength = 64;

    private static readonly EmailAddressAttribute EmailFormat = new();

    private readonly IMemberAccounts _accounts;
    private readonly TimeProvider _time;

    public SignUpViewModel(IMemberAccounts accounts, DeviceSession session, TimeProvider time)
    {
        _accounts = accounts;
        _time = time;
        session.SignedInChanged += (_, _) => Clear();
    }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = "";

    [ObservableProperty]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    /// <summary>The date picker's value. Only its date counts; null until the member picks one.</summary>
    [ObservableProperty]
    public partial DateTime? BirthDate { get; set; }

    /// <summary>Why the last attempt didn't go through, for the member to read; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>The account exists and the confirmation email is on its way, so the form gives way to that message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormVisible))]
    public partial bool IsConfirmationSent { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    public bool IsFormVisible => !IsConfirmationSent;

    /// <summary>The latest birth date the picker offers: today on the device.</summary>
    public DateTime Today => _time.GetLocalNow().Date;

    [RelayCommand]
    private async Task SignUpAsync(CancellationToken cancellationToken)
    {
        // Cleared first, so a repeated message is announced again.
        Error = null;
        var zone = _time.LocalTimeZone;
        Error = Validate(zone);
        if (Error is not null)
        {
            return;
        }

        IsBusy = true;

        // Let the screen show the busy state before the call (Blazor spike §5 rule for view-model commands).
        await Task.Yield();

        try
        {
            var request = new SignUpRequest(
                Email.Trim(), Password, DisplayName.Trim(), DateOnly.FromDateTime(BirthDate!.Value), IanaId(zone));
            var status = await _accounts.SignUpAsync(request, cancellationToken);
            if (status == SignUpStatus.ConfirmationSent)
            {
                // The password has done its job; don't keep it on screen or in memory.
                Password = "";
                IsConfirmationSent = true;
            }
            else
            {
                Error = SignUpMessages.For(status);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The member left the screen; nothing to show.
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Back to the form from the check-email message, keeping what the member typed except the password.</summary>
    [RelayCommand]
    private void SignUpAgain()
    {
        Error = null;
        IsConfirmationSent = false;
    }

    private void Clear()
    {
        DisplayName = "";
        Email = "";
        Password = "";
        BirthDate = null;
        Error = null;
        IsConfirmationSent = false;
    }

    private string? Validate(TimeZoneInfo zone)
    {
        var name = DisplayName.Trim();
        var email = Email.Trim();

        if (name.Length == 0)
        {
            return AccountStrings.NameRequired;
        }

        if (name.Length > MaxNameLength)
        {
            return AccountStrings.NameTooLong;
        }

        if (email.Length == 0)
        {
            return AccountStrings.EmailRequired;
        }

        if (email.Length > MaxEmailLength || !EmailFormat.IsValid(email))
        {
            return AccountStrings.EmailInvalid;
        }

        if (Password.Length == 0)
        {
            return AccountStrings.PasswordRequired;
        }

        if (Password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            return AccountStrings.PasswordLength;
        }

        if (BirthDate is not { } birthDate)
        {
            return AccountStrings.BirthDateRequired;
        }

        // On the member's own calendar, as the database checks it.
        var today = LocalCalendar.DateAt(_time.GetUtcNow(), zone);
        return LocalCalendar.IsOldEnough(DateOnly.FromDateTime(birthDate), today) ? null : AccountStrings.TooYoung;
    }

    /// <summary>
    /// The zone's IANA name, which the profile stores. Android, iOS and macOS use IANA names already; Windows uses its own,
    /// which convert. The database falls back to UTC for a name it doesn't know, and the member can change it later.
    /// </summary>
    private static string IanaId(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana
        : "UTC";
}

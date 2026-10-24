using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitCore.Client.Core.Resources;

namespace FitCore.Client.Core.Accounts;

/// <summary>
/// Email sign-in on the device apps (implementation plan PR 4), with the web form's rules and messages. A sign-in
/// that works raises <see cref="DeviceSession.SignedInChanged"/>, and the app moves on from there.
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
    private const int MaxEmailLength = 254;

    private static readonly EmailAddressAttribute EmailFormat = new();

    private readonly DeviceSession _session;

    public SignInViewModel(DeviceSession session)
    {
        _session = session;
    }

    [ObservableProperty]
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    /// <summary>Why the last attempt didn't go through, for the member to read; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        // Cleared first, so a repeated message is announced again.
        Error = null;
        Error = Validate();
        if (Error is not null)
        {
            return;
        }

        IsBusy = true;

        // Let the screen show the busy state before the call (Blazor spike §5 rule for view-model commands).
        await Task.Yield();

        try
        {
            var status = await _session.SignInAsync(Email.Trim(), Password, cancellationToken);
            if (status == SignInStatus.SignedIn)
            {
                // Don't keep the password on screen or in memory, including for the next member on this device.
                Password = "";
            }
            else
            {
                Error = SignInMessages.For(status);
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

    private string? Validate()
    {
        var email = Email.Trim();

        if (email.Length == 0)
        {
            return AccountStrings.EmailRequired;
        }

        if (email.Length > MaxEmailLength || !EmailFormat.IsValid(email))
        {
            return AccountStrings.EmailInvalid;
        }

        // No length rule here: a password from before a rule change must still sign in.
        return Password.Length == 0 ? AccountStrings.CurrentPasswordRequired : null;
    }
}

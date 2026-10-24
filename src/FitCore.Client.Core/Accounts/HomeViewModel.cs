using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FitCore.Client.Core.Resources;

namespace FitCore.Client.Core.Accounts;

/// <summary>
/// The signed-in home screen on the device apps. For now it only signs out; the R1 screens join it as their PRs land.
/// A sign-out that works raises <see cref="DeviceSession.SignedInChanged"/>, and the app returns to sign-in.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly DeviceSession _session;

    public HomeViewModel(DeviceSession session)
    {
        _session = session;
    }

    /// <summary>Why the last sign-out didn't go through; null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        Error = null;
        IsBusy = true;
        await Task.Yield();

        try
        {
            if (await _session.SignOutAsync(cancellationToken) == SignOutStatus.Unavailable)
            {
                // The web's message: the member is still signed in and can try again.
                Error = AccountStrings.SignOutFailedBody;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }
}

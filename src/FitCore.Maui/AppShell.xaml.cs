using FitCore.Client.Core.Accounts;

namespace FitCore.Maui;

public partial class AppShell : Shell
{
    // Absolute routes, so moving between them replaces the screen instead of stacking a back button.
    public const string SignInRoute = "//sign-in";
    public const string SignUpRoute = "//sign-up";
    public const string HomeRoute = "//home";

    private readonly DeviceSession _session;

    public AppShell(DeviceSession session)
    {
        InitializeComponent();
        _session = session;

        // Sign-in, sign-out, and a session Supabase Auth ended, from any screen.
        _session.SignedInChanged += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await GoToAsync(_session.IsSignedIn ? HomeRoute : SignInRoute));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        var signedIn = await _session.RestoreAsync();
        await GoToAsync(signedIn ? HomeRoute : SignInRoute);
    }
}

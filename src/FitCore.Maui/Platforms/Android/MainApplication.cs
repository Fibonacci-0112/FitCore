using Android.App;
using Android.Runtime;

namespace FitCore.Maui;

#if DEBUG
// Debug builds talk to the local Supabase stack over plain HTTP (FitCore.Maui.csproj). Release builds allow only HTTPS.
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

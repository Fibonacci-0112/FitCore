using System.Reflection;
using FitCore.Client.Auth;
using FitCore.Client.Core.Accounts;
using FitCore.Maui.Pages;

namespace FitCore.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new SupabaseAuthOptions(new Uri(Setting("SupabaseUrl")), Setting("SupabasePublishableKey")));
        builder.Services.AddSingleton<IMemberAccounts, SupabaseMemberAccounts>();
        builder.Services.AddTransient<SignUpViewModel>();
        builder.Services.AddTransient<SignUpPage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }

    /// <summary>A build setting from FitCore.Maui.csproj, compiled into the app as assembly metadata.</summary>
    private static string Setting(string key) =>
        typeof(MauiProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The app was built without {key} (FitCore.Maui.csproj).");
}

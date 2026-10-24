using FitCore.Testing;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace FitCore.Maui.UITests;

/// <summary>
/// The FitCore Debug APK on a running Android emulator, driven by an Appium server with the UiAutomator2 driver, and the
/// local Supabase database the app's Auth calls reach through the emulator's host address (10.0.2.2). Set
/// FITCORE_ANDROID_APK to the APK's path to run; FITCORE_APPIUM_URL overrides the server's address, and
/// FITCORE_UITEST_ARTIFACTS names a directory for a screenshot and the screen's layout when a wait times out. Every member
/// these tests sign up uses an <c>@android.test</c> address and is deleted afterwards.
/// </summary>
public sealed class AndroidApp : IAsyncLifetime
{
    public const string Package = "app.fitcore";

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    private AndroidDriver? _driver;

    public static string? ApkPath => Environment.GetEnvironmentVariable("FITCORE_ANDROID_APK") is { Length: > 0 } path ? path : null;

    /// <summary>Whether there is an app to test; the tests are skipped otherwise.</summary>
    public static bool Configured => ApkPath is not null;

    public AndroidDriver Driver => _driver ?? throw new InvalidOperationException("No app: set FITCORE_ANDROID_APK.");

    public LocalDatabase Database { get; } = new();

    public static string NewEmail() => $"{Guid.NewGuid():N}@android.test";

    public async ValueTask InitializeAsync()
    {
        if (ApkPath is not { } apk)
        {
            return;
        }

        await Database.InitializeAsync();

        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            App = Path.GetFullPath(apk),
        };

        // A keyboard that never shows, so no field or button sits behind it.
        options.AddAdditionalAppiumOption("appium:hideKeyboard", true);

        // The emulator on a CI runner is slow to install and start the app the first time.
        options.AddAdditionalAppiumOption("appium:uiautomator2ServerInstallTimeout", 120_000);
        options.AddAdditionalAppiumOption("appium:adbExecTimeout", 120_000);
        options.AddAdditionalAppiumOption("appium:appWaitDuration", 120_000);
        options.AddAdditionalAppiumOption("appium:newCommandTimeout", 300);

        var server = new Uri(Environment.GetEnvironmentVariable("FITCORE_APPIUM_URL") ?? "http://127.0.0.1:4723");
        _driver = new AndroidDriver(server, options, TimeSpan.FromMinutes(5));
    }

    public async ValueTask DisposeAsync()
    {
        _driver?.Quit();
        _driver?.Dispose();
        if (Configured)
        {
            await Database.ExecuteAsync("delete from auth.users where email like '%@android.test'");
        }

        await Database.DisposeAsync();
    }

    /// <summary>
    /// The element with the MAUI <c>AutomationId</c>, once it is on screen, scrolling to it if it's below the fold.
    /// Saves a screenshot and the screen's layout before giving up, so a CI failure shows what the app showed.
    /// </summary>
    public AppiumElement WaitFor(string automationId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultWait);
        while (DateTime.UtcNow < deadline)
        {
            if (Driver.FindElements(MobileBy.Id(automationId)) is [{ Displayed: true } element, ..])
            {
                return element;
            }

            // Below the fold of a scrolling page: UiAutomator2 only finds what is on screen.
            Driver.FindElements(MobileBy.AndroidUIAutomator(
                "new UiScrollable(new UiSelector().scrollable(true))" +
                $".scrollIntoView(new UiSelector().resourceIdMatches(\"(.*:id/)?{automationId}\"))"));
            Thread.Sleep(500);
        }

        SaveScreen(automationId);
        throw new TimeoutException($"{automationId} didn't appear within {(timeout ?? DefaultWait).TotalSeconds} seconds.");
    }

    /// <summary>Waits until the element with the <c>AutomationId</c> is gone from the screen.</summary>
    public void WaitUntilGone(string automationId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultWait);
        while (DateTime.UtcNow < deadline)
        {
            if (Driver.FindElements(MobileBy.Id(automationId)) is not [{ Displayed: true }, ..])
            {
                return;
            }

            Thread.Sleep(500);
        }

        SaveScreen(automationId + "-still-shown");
        throw new TimeoutException($"{automationId} was still on screen after {(timeout ?? DefaultWait).TotalSeconds} seconds.");
    }

    /// <summary>Types into a field after clearing it.</summary>
    public void Type(string automationId, string text)
    {
        var field = WaitFor(automationId);
        field.Clear();
        field.SendKeys(text);
    }

    /// <summary>
    /// Picks a year in Android's date picker dialog, opened from the MAUI DatePicker with the <c>AutomationId</c>. The
    /// month and day stay at the dialog's: today's.
    /// </summary>
    public void PickYear(string automationId, int year)
    {
        WaitFor(automationId).Click();

        // The dialog opens on the calendar; the year in its header opens the list of years, on this year.
        WaitForAndroid("android:id/date_picker_header_year").Click();

        // Earlier years are above, so scroll the list (the dialog's only scrolling view) up until the year shows.
        var wanted = MobileBy.AndroidUIAutomator($"new UiSelector().text(\"{year}\")");
        var list = MobileBy.AndroidUIAutomator("new UiSelector().scrollable(true)");
        for (var step = 0; step < 60; step++)
        {
            if (Driver.FindElements(wanted) is [var item, ..])
            {
                item.Click();
                WaitForAndroid("android:id/button1").Click();
                return;
            }

            if (Driver.FindElements(list) is not [var years, ..])
            {
                Thread.Sleep(500);
                continue;
            }

            Driver.ExecuteScript("mobile: scrollGesture", new Dictionary<string, object>
            {
                ["elementId"] = years.Id,
                ["direction"] = "up",
                ["percent"] = 0.75,
            });
        }

        SaveScreen("year-" + year);
        throw new TimeoutException($"The date picker never showed {year}.");
    }

    private AppiumElement WaitForAndroid(string resourceId)
    {
        var deadline = DateTime.UtcNow + DefaultWait;
        while (DateTime.UtcNow < deadline)
        {
            if (Driver.FindElements(MobileBy.Id(resourceId)) is [var element, ..])
            {
                return element;
            }

            Thread.Sleep(500);
        }

        SaveScreen(resourceId.Replace(':', '-').Replace('/', '-'));
        throw new TimeoutException($"{resourceId} didn't appear within {DefaultWait.TotalSeconds} seconds.");
    }

    private void SaveScreen(string name)
    {
        if (Environment.GetEnvironmentVariable("FITCORE_UITEST_ARTIFACTS") is not { Length: > 0 } directory)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            Driver.GetScreenshot().SaveAsFile(Path.Combine(directory, name + ".png"));
            File.WriteAllText(Path.Combine(directory, name + ".xml"), Driver.PageSource);
        }
        catch (WebDriverException)
        {
            // The app or the session is gone; the timeout says enough.
        }
    }
}

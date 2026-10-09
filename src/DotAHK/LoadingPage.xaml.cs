using DotAHK.Services;
using DotAHK.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DotAHK;

/// <summary>
/// Transient page shown the instant the window is activated. It performs no I/O in
/// its constructor; it awaits the background service initialization and then routes
/// to the onboarding flow or the dashboard. This guarantees the window paints
/// immediately on launch.
/// </summary>
public sealed partial class LoadingPage : Page
{
    /// <summary>Localized text surface for the XAML bindings (x:Uid cannot follow a runtime override).</summary>
    public LocalizedStrings Loc { get; } = new();

    public LoadingPage()
    {
        StartupTrace.Mark("LoadingPage ctor enter");
        InitializeComponent();
        Loaded += OnLoaded;
        StartupTrace.Mark("LoadingPage ctor exit");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        StartupTrace.Mark("LoadingPage.OnLoaded entered (window should now be visible)");

        try
        {
            // Idempotent: shares the task started in App.OnLaunched. Runs off-thread.
            await AppServices.EnsureInitializedAsync();
            StartupTrace.Mark("services initialized");

            // Resolve the UI language (first-run OS detection or the saved choice) before
            // the localized pages are created so their x:Uid resolves correctly.
            LocalizationService.Initialize(AppServices.Settings);
            StartupTrace.Mark("language initialized");
        }
        catch (Exception ex)
        {
            StartupTrace.Mark("startup init failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        var target = AppServices.Settings.Settings.HasCompletedOnboarding
            ? typeof(MainPage)
            : typeof(OnboardingPage);

        Frame.Navigate(target);
        StartupTrace.Mark("navigated to " + target.Name);
    }
}

using DotAHK.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DotAHK;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    static App()
    {
        StartupTrace.Start("process launch");

        // Safety net: if the process ends without going through the normal tray/window
        // exit path, still kill every AutoHotkey process this app spawned.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppServices.Shutdown();
    }

    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// True when the process was started with the <c>--minimized</c> switch (used
    /// by the daemon auto-start registration) so the window boots into the tray.
    /// </summary>
    public static bool StartMinimized { get; private set; }

    /// <summary>
    /// The Windows App SDK instance handle. Kept in a static field for the lifetime of the
    /// process so it is not garbage collected (the AppLifecycle API requires a live
    /// reference) and so redirected activations keep being delivered to the main instance.
    /// </summary>
    private static AppInstance? _appInstance;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Capture the UI dispatcher before any window/page is created: pages build
        // their view models during construction and redirected activations are marshaled
        // through it.
        StartupTrace.Mark("OnLaunched entered");
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        StartMinimized = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        // --- Strict single instance -----------------------------------------------
        // Enforce one running process so the ProcessTracker and the settings file are
        // never owned by two instances at once. A duplicate redirects its activation to
        // the first instance (which then surfaces its window) and exits immediately.
        try
        {
            _appInstance = AppInstance.FindOrRegisterForKey("DotAHK_SingleInstance");
            if (!_appInstance.IsCurrent)
            {
                // The daemon auto-start uses --minimized: an instance is already running,
                // so there is nothing to do and the user's foreground window must not be
                // disturbed. Only a real user launch is redirected to raise the window.
                if (!StartMinimized)
                {
                    var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                    await _appInstance.RedirectActivationToAsync(activationArgs);
                    StartupTrace.Mark("single instance: activation redirected to main");
                }
                else
                {
                    StartupTrace.Mark("single instance: duplicate --minimized launch, exiting");
                }

                // This duplicate owns nothing: never touch ProcessTracker or settings.
                Environment.Exit(0);
                return;
            }

            _appInstance.Activated += OnRedirectedActivation;
            StartupTrace.Mark("single instance: registered as the main instance");
        }
        catch (Exception ex)
        {
            // If the AppLifecycle API is unavailable the app still starts (just without the
            // single-instance guarantee) rather than failing to launch.
            StartupTrace.Mark("single instance setup failed: " + ex.GetType().Name);
        }

        // Apply the language BEFORE any window is created so the bound UI text is already
        // correct when the first page is constructed.
        LocalizationService.ApplyEarly();

        Window = new MainWindow();
        StartupTrace.Mark("MainWindow constructed");
        Window.Activate();
        StartupTrace.Mark("Window.Activate returned (first paint queued)");

        // Start the (idempotent) background service initialization right away so the
        // window can paint immediately and the daemon path still initializes even
        // when the window is hidden straight into the notification area.
        _ = AppServices.EnsureInitializedAsync();
        StartupTrace.Mark("background init kicked off");

        if (StartMinimized && Window is MainWindow mainWindow)
        {
            mainWindow.MinimizeToTray();
        }
    }

    /// <summary>
    /// Raised on the main instance when another process redirects its activation here
    /// (i.e. the user tried to launch a second instance). Brings the existing window to
    /// the foreground, restoring it from the notification area or a minimized state.
    /// </summary>
    private void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        StartupTrace.Mark("redirected activation received");

        var dispatcher = DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            SurfaceMainWindow();
        }
        else
        {
            dispatcher.TryEnqueue(SurfaceMainWindow);
        }
    }

    private static void SurfaceMainWindow()
    {
        if (Window is MainWindow mainWindow)
        {
            mainWindow.RestoreAndActivate();
        }
    }
}

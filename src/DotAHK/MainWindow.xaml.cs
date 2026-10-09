using System.IO;
using System.Runtime.InteropServices;
using DotAHK.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DotAHK;

/// <summary>
/// The application window. The constructor is intentionally pure: it only builds the
/// visual tree. Every piece of shell setup (title bar, icon, tray icon, navigation)
/// is deferred to the first <see cref="Window.Activated"/> event so the window is
/// created and shown with no file I/O, registry access or navigation on the
/// construction path.
/// </summary>
public sealed partial class MainWindow : Window
{
    private TrayIcon? _trayIcon;
    private bool _reallyExit;
    private bool _closeDialogShown;
    private bool _shellInitialized;
    private bool _pendingMinimizeToTray;

    public MainWindow()
    {
        // Constructor purity: only InitializeComponent() and a trivial assignment.
        InitializeComponent();
        Activated += OnWindowActivated;
    }

    /// <summary>
    /// Runs once, the first time the window becomes active (i.e. it is on screen).
    /// Doing the shell setup here keeps the fast construction path free of work so the
    /// window paints immediately.
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_shellInitialized)
        {
            return;
        }

        _shellInitialized = true;
        Activated -= OnWindowActivated;

        StartupTrace.Mark("window activated: configuring shell");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Use an absolute path so the window/taskbar icon loads regardless of the
        // process working directory (which is not guaranteed to be the EXE folder).
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "DotAHK.ico"));

        // Every close is intercepted so the user can choose to keep DotAHK alive
        // in the notification area instead of quitting.
        AppWindow.Closing += OnAppWindowClosing;

        InitializeTrayIcon();

        if (_pendingMinimizeToTray)
        {
            _pendingMinimizeToTray = false;
            MinimizeToTray();
        }

        // Now that the window is on screen, bring up the loading page which awaits the
        // background service initialization and routes to onboarding or the dashboard.
        RootFrame.Navigate(typeof(LoadingPage));
        StartupTrace.Mark("shell ready: LoadingPage navigated");
    }

    private void InitializeTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "DotAHK.ico");
            _trayIcon = new TrayIcon(iconPath, "DotAHK");
            _trayIcon.OpenRequested += (_, _) => ShowMainWindow();
            _trayIcon.ExitRequested += (_, _) => _ = ExitApplicationAsync();
        }
        catch
        {
            // A tray icon is a convenience: if it cannot be created the app still works.
            _trayIcon = null;
        }
    }

    private void ShowMainWindow()
    {
        AppWindow.Show();
        Activate();
    }

    /// <summary>
    /// Brings the window to the foreground from any state - hidden in the notification
    /// area, minimized, or simply behind other windows. Invoked when a second instance
    /// redirects its activation to this one so the user always sees the existing window
    /// instead of a silent no-op.
    /// </summary>
    public void RestoreAndActivate()
    {
        // A redirect means the user wants the window right now: cancel any pending
        // minimize-to-tray requested by a --minimized start-up before the shell was ready.
        _pendingMinimizeToTray = false;

        // Un-hide if it was parked in the notification area.
        AppWindow.Show();

        // Restore from minimized and force it to the foreground. Activate() alone cannot
        // reliably raise a window that is owned by another process's foreground context,
        // so the Win32 calls are used in addition.
        var hwnd = App.WindowHandle;
        ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
        Activate();
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    /// <summary>
    /// Hides the window straight into the notification area. Used when the process is
    /// launched through the <c>--minimized</c> daemon switch. If the tray icon is not
    /// ready yet (the shell has not initialised) the request is remembered and applied
    /// as soon as it is.
    /// </summary>
    public void MinimizeToTray()
    {
        if (_trayIcon is null)
        {
            _pendingMinimizeToTray = true;
            return;
        }

        AppWindow.Hide();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_reallyExit)
        {
            return;
        }

        // Never let the window close until the user has decided.
        args.Cancel = true;

        if (_closeDialogShown)
        {
            return;
        }

        // Without a tray icon there is no way to bring the window back, so just quit.
        if (_trayIcon is null)
        {
            await ExitApplicationAsync();
            return;
        }

        _closeDialogShown = true;
        try
        {
            var keepRunning = await AppServices.Dialogs.ConfirmAsync(
                LocalizationService.Get("DialogKeepRunningTitle"),
                LocalizationService.Get("DialogKeepRunningMessage"),
                LocalizationService.Get("DialogYes"),
                LocalizationService.Get("DialogNo"));

            if (keepRunning)
            {
                AppWindow.Hide();
            }
            else
            {
                await ExitApplicationAsync();
            }
        }
        catch
        {
            // If the dialog cannot be shown for any reason, fall back to a clean exit.
            await ExitApplicationAsync();
        }
        finally
        {
            _closeDialogShown = false;
        }
    }

    /// <summary>
    /// Kills every tracked AutoHotkey process and releases global hotkeys so no orphan
    /// processes survive, removes the tray icon and closes the app.
    /// </summary>
    private Task ExitApplicationAsync()
    {
        // Best-effort, idempotent: stops all children spawned by the tracker.
        AppServices.Shutdown();

        _trayIcon?.Dispose();
        _trayIcon = null;

        _reallyExit = true;
        Close();

        return Task.CompletedTask;
    }
}

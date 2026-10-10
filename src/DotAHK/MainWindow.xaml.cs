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
    /// Prepares a <c>--minimized</c> (daemon/auto-start) launch so the window is never
    /// painted on screen. The native HWND is hidden as early as possible - before the
    /// UI thread has a chance to render the visual tree - and the shell (tray icon,
    /// close interception and navigation) is brought up eagerly because the
    /// <see cref="Window.Activated"/> event never fires for a window that stays hidden.
    /// </summary>
    public void PrepareHiddenStartup()
    {
        // Grab the AppWindow from this window's native handle and hide the window at the
        // Win32 level immediately. ShowWindow(SW_HIDE) prevents the framework from
        // presenting the default frame before it is parked in the notification area.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SwHide);

        // Belt-and-braces: also hide through the AppWindow abstraction so the presenter
        // state matches the native visibility.
        AppWindow.Hide();

        // The window must remain parked; remember that so the shell setup below keeps it
        // hidden even if the tray icon is created slightly later.
        _pendingMinimizeToTray = true;

        // Activated never fires while the window stays hidden, so initialize the shell
        // (tray icon, close handler, loading page) directly instead of waiting for it.
        InitializeShell();

        StartupTrace.Mark("minimized start: window hidden before first render");
    }

    /// <summary>
    /// Runs once, the first time the window becomes active (i.e. it is on screen).
    /// Doing the shell setup here keeps the fast construction path free of work so the
    /// window paints immediately.
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        InitializeShell();
    }

    /// <summary>
    /// One-time shell configuration: title bar, icon, tray icon, close interception and
    /// the loading page navigation. Safe to call from either the activation path or the
    /// hidden start-up path; it is idempotent.
    /// </summary>
    private void InitializeShell()
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

        // Bring up the loading page which awaits the background service initialization
        // and routes to onboarding or the dashboard. This runs even while the window is
        // hidden so the app is fully functional in the notification area.
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
    private const int SwHide = 0;

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

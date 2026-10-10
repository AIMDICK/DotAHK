using DotAHK.Services;
using DotAHK.ViewModels;

namespace DotAHK;

/// <summary>
/// Minimal, dependency-free composition root. The service graph is built once on a
/// background thread by <see cref="EnsureInitializedAsync"/> so that no settings
/// I/O (file reads) ever runs on the UI thread during startup. This keeps the
/// window painting instantly while initialization happens in the background.
/// </summary>
public static class AppServices
{
    private static readonly object Sync = new();
    private static Task? _initialization;
    private static bool _shutdown;

    private static ISettingsService? _settings;
    private static IAhkInstallationService? _installations;
    private static IScriptScanner? _scanner;
    private static ITempScriptService? _tempScripts;
    private static IProcessTracker? _tracker;
    private static IDialogService? _dialogs;
    private static IFileLocationService? _fileLocation;
    private static IEditorService? _editor;
    private static IProfileService? _profiles;
    private static IGlobalHotkeyService? _hotkeys;
    private static IAutoStartService? _autoStart;
    private static IAdminElevationService? _adminElevation;
    private static IStartupTaskService? _startupTask;

    /// <summary>True once the service graph has been constructed.</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Builds the service graph on a background thread. Idempotent and safe to call
    /// from multiple threads: repeated/concurrent calls return the same task. The
    /// method never blocks the caller because the construction happens inside
    /// <see cref="Task.Run(Action)"/>.
    /// </summary>
    public static Task EnsureInitializedAsync()
    {
        if (_initialization is not null)
        {
            return _initialization;
        }

        lock (Sync)
        {
            _initialization ??= Task.Run(() =>
            {
                StartupTrace.Mark("service graph build started (background thread)");
                // SettingsService performs a synchronous file read in its constructor;
                // running it here keeps that I/O off the UI thread.
                var settings = new SettingsService();
                var installations = new AhkInstallationService();
                var tempScripts = new TempScriptService();

                _settings = settings;
                _installations = installations;
                _scanner = new ScriptScanner(installations);
                _tempScripts = tempScripts;
                _tracker = new ProcessTracker(tempScripts, settings);
                _dialogs = new DialogService();
                _fileLocation = new FileLocationService();
                _editor = new EditorService();
                _profiles = new ProfileService(settings);
                _hotkeys = new GlobalHotkeyService();
                _autoStart = new AutoStartService();
                _adminElevation = new AdminElevationService();
                _startupTask = new StartupTaskService();

                IsInitialized = true;
                StartupTrace.Mark("service graph build finished");
            });

            return _initialization;
        }
    }

    public static ISettingsService Settings => _settings ?? NotInitialized<ISettingsService>();

    public static IAhkInstallationService Installations => _installations ?? NotInitialized<IAhkInstallationService>();

    public static IScriptScanner Scanner => _scanner ?? NotInitialized<IScriptScanner>();

    public static ITempScriptService TempScripts => _tempScripts ?? NotInitialized<ITempScriptService>();

    public static IProcessTracker Tracker => _tracker ?? NotInitialized<IProcessTracker>();

    public static IDialogService Dialogs => _dialogs ?? NotInitialized<IDialogService>();

    public static IFileLocationService FileLocation => _fileLocation ?? NotInitialized<IFileLocationService>();

    public static IEditorService Editor => _editor ?? NotInitialized<IEditorService>();

    public static IProfileService Profiles => _profiles ?? NotInitialized<IProfileService>();

    public static IGlobalHotkeyService Hotkeys => _hotkeys ?? NotInitialized<IGlobalHotkeyService>();

    public static IAutoStartService AutoStart => _autoStart ?? NotInitialized<IAutoStartService>();

    public static IAdminElevationService AdminElevation =>
        _adminElevation ?? NotInitialized<IAdminElevationService>();

    public static IStartupTaskService StartupTask =>
        _startupTask ?? NotInitialized<IStartupTaskService>();

    public static MainViewModel CreateMainViewModel() =>
        new(Scanner, Installations, Tracker, Settings, Dialogs, FileLocation, Editor,
            Profiles, Hotkeys, AutoStart, AdminElevation, StartupTask);

    public static OnboardingViewModel CreateOnboardingViewModel() =>
        new(Settings);

    /// <summary>
    /// Best-effort cleanup for application exit: kills every AutoHotkey process this
    /// app spawned (leaving no orphans) and releases global hotkey registrations.
    /// Idempotent, safe to call from any thread, and never throws.
    /// </summary>
    public static void Shutdown()
    {
        lock (Sync)
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;
        }

        try
        {
            _tracker?.StopAllAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown must never throw.
        }

        try
        {
            (_hotkeys as IDisposable)?.Dispose();
        }
        catch
        {
        }
    }

    private static T NotInitialized<T>() =>
        throw new InvalidOperationException(
            "AppServices have not been initialized. Await AppServices.EnsureInitializedAsync() first.");
}

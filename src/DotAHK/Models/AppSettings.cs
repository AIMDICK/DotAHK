namespace DotAHK.Models;

/// <summary>
/// Persisted, user-configurable application state.
/// </summary>
public sealed class AppSettings
{
    /// <summary>True once the user has completed (or skipped) the onboarding flow.</summary>
    public bool HasCompletedOnboarding { get; set; }

    /// <summary>Folders that are scanned for AutoHotkey scripts (the "Watch List").</summary>
    public List<string> WatchFolders { get; set; } = new();

    /// <summary>Burst-mode duration in seconds (fixed default of 10 seconds per specification).</summary>
    public int BurstDurationSeconds { get; set; } = 10;

    /// <summary>
    /// When true (the default), scripts are launched through a temporary execution
    /// copy that has <c>#NoTrayIcon</c> injected, so individual scripts never add
    /// their own notification-area icons. The application shows a single master
    /// tray icon instead.
    /// </summary>
    public bool TrayHijackingEnabled { get; set; } = true;

    /// <summary>
    /// User-defined launch arguments per script, keyed by the normalized
    /// (lower-case, full) script path. Empty strings are stored as-is.
    /// </summary>
    public Dictionary<string, string> ScriptArguments { get; set; } = new();

    /// <summary>
    /// User-defined global hotkey per script (for example "Ctrl+Alt+G"), keyed by
    /// the normalized script path. Pressing the hotkey toggles the script on/off.
    /// </summary>
    public Dictionary<string, string> ScriptHotkeys { get; set; } = new();

    /// <summary>
    /// Environment profiles ("Gaming", "Dev", ...) that group scripts together.
    /// </summary>
    public List<EnvironmentProfile> Profiles { get; set; } = new();

    /// <summary>Name of the profile that was last activated, if any.</summary>
    public string? ActiveProfileName { get; set; }

    /// <summary>
    /// Normalized script paths flagged "Launch on startup". When DotAHK boots it
    /// automatically runs every script in this list that has an interpreter.
    /// </summary>
    public List<string> AutoStartScripts { get; set; } = new();

    /// <summary>
    /// Normalized script paths the user has hidden (soft delete). Hidden scripts
    /// remain on disk and are simply removed from the active list; they can be
    /// restored from the "Hidden scripts" panel.
    /// </summary>
    public List<string> IgnoredScripts { get; set; } = new();

    /// <summary>True when DotAHK is registered to launch automatically on Windows boot.</summary>
    public bool LaunchOnStartup { get; set; }

    /// <summary>
    /// Persisted UI language (for example "en-US"). Null until the first launch has run
    /// the OS-language detection; the value is remembered so it is not re-evaluated on
    /// every boot and can be overridden by the user's manual selection.
    /// </summary>
    public string? Language { get; set; }
}

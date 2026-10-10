using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotAHK.Models;
using DotAHK.Services;
using Microsoft.UI.Xaml;

namespace DotAHK.ViewModels;

/// <summary>
/// ViewModel for a single script row on the dashboard. Owns the Active toggle,
/// the Burst command, the Schedule command and the live status/countdown text.
/// </summary>
public partial class ScriptItemViewModel : ObservableObject
{
    private readonly IProcessTracker _tracker;
    private readonly IFileLocationService _fileLocation;
    private readonly ISettingsService _settings;
    private readonly IProfileService _profiles;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly IAdminElevationService _adminElevation;
    private bool _suppressToggleHandling;
    private RunMode _activeMode = RunMode.Persistent;

    public ScriptItemViewModel(
        AhkScript script,
        IProcessTracker tracker,
        IFileLocationService fileLocation,
        ISettingsService settings,
        IProfileService profiles,
        IGlobalHotkeyService hotkeys,
        IAdminElevationService adminElevation)
    {
        Script = script;
        _tracker = tracker;
        _fileLocation = fileLocation;
        _settings = settings;
        _profiles = profiles;
        _hotkeys = hotkeys;
        _adminElevation = adminElevation;

        // Seed the argument box from persisted settings without triggering a save.
        _arguments = settings.Settings.ScriptArguments.TryGetValue(script.Key, out var stored)
            ? stored
            : string.Empty;

        _hotkeyText = settings.Settings.ScriptHotkeys.TryGetValue(script.Key, out var hotkey)
            ? hotkey
            : string.Empty;

        _launchOnStartup = settings.Settings.AutoStartScripts
            .Contains(script.Key, StringComparer.OrdinalIgnoreCase);

        RefreshGroupMembership();

        // Bind any hotkey that was persisted from a previous session.
        if (!string.IsNullOrWhiteSpace(_hotkeyText))
        {
            ApplyHotkey(_hotkeyText);
        }
    }

    /// <summary>Localized strings bound by the card's XAML (see <see cref="LocalizedStrings"/>).</summary>
    public LocalizedStrings Loc { get; } = new();

    public AhkScript Script { get; }

    public string Name => Script.FileName;

    public string FilePath => Script.FilePath;

    public string DirectoryPath => Script.DirectoryPath;

    /// <summary>Short "v1"/"v2" badge shown next to the file name.</summary>
    public string VersionLabel => Script.DetectedVersion switch
    {
        AhkVersion.V2 => "v2",
        AhkVersion.V1 => "v1",
        _ => Script.Installation?.Version switch
        {
            AhkVersion.V2 => "v2",
            AhkVersion.V1 => "v1",
            _ => "?",
        },
    };

    public string InstallationLabel => Script.Installation is null
        ? LocalizationService.Get("CardInstallationMissing")
        : $"AutoHotkey {Script.Installation.DisplayVersion ?? Script.Installation.Version.ToString()}";

    public bool CanRun => Script.Installation is not null;

    /// <summary>True when the script source appears to request UAC elevation itself.</summary>
    public bool RequiresAdmin => Script.RequiresAdmin;

    /// <summary>
    /// Warning-icon visibility: shown only when the script requests elevation while
    /// DotAHK itself is NOT running elevated. In that case Windows' UAC boundary hides
    /// the elevated child from the tracking handle, so the active toggle would falsely
    /// flip back to off. Returned as <see cref="Visibility"/> for direct XAML binding,
    /// with no value converter.
    /// </summary>
    public Visibility ShowAdminWarning =>
        RequiresAdmin && !_adminElevation.IsAdministrator
            ? Visibility.Visible
            : Visibility.Collapsed;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>True when the script is hidden (soft-deleted) from the active list.</summary>
    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = LocalizationService.Get("CardInactive");

    [ObservableProperty]
    private string _countdownText = string.Empty;

    /// <summary>User-defined command-line arguments passed to the script on launch.</summary>
    [ObservableProperty]
    private string _arguments = string.Empty;

    partial void OnArgumentsChanged(string value)
    {
        var normalized = value ?? string.Empty;
        if (_settings.Settings.ScriptArguments.TryGetValue(Script.Key, out var existing) &&
            string.Equals(existing, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Settings.ScriptArguments[Script.Key] = normalized;
        _settings.Save();
    }

    // ---- Global hotkey ---------------------------------------------------

    /// <summary>User-assigned global hotkey in "Ctrl+Alt+G" form (empty = none).</summary>
    [ObservableProperty]
    private string _hotkeyText = string.Empty;

    /// <summary>Result of the last hotkey registration attempt, shown as a hint.</summary>
    [ObservableProperty]
    private string _hotkeyStatus = string.Empty;

    partial void OnHotkeyTextChanged(string value)
    {
        var normalized = (value ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            _settings.Settings.ScriptHotkeys.Remove(Script.Key);
        }
        else
        {
            _settings.Settings.ScriptHotkeys[Script.Key] = normalized;
        }

        _settings.Save();
        ApplyHotkey(normalized);
    }

    /// <summary>
    /// Registers (or clears) the global hotkey for this script and updates the
    /// status hint. Never throws; failures are surfaced through
    /// <see cref="HotkeyStatus"/>.
    /// </summary>
    private void ApplyHotkey(string text)
    {
        _hotkeys.Unregister(Script.Key);

        if (string.IsNullOrWhiteSpace(text))
        {
            HotkeyStatus = LocalizationService.Get("HotkeyNone");
            return;
        }

        if (!HotkeyParser.TryParse(text, out var gesture) || gesture is null)
        {
            HotkeyStatus = LocalizationService.Get("HotkeyInvalid");
            return;
        }

        HotkeyStatus = _hotkeys.TryRegister(Script.Key, gesture, out var error)
            ? LocalizationService.Format("HotkeyBound", gesture.DisplayText)
            : LocalizationService.Format("HotkeyError", error);
    }

    /// <summary>Native hotkeys detected in the script source, shown as read-only tags.</summary>
    public IReadOnlyList<string> DetectedHotkeys => Script.DetectedHotkeys;

    public bool HasDetectedHotkeys => Script.DetectedHotkeys.Count > 0;

    // ---- Groups (many-to-many) ------------------------------------------

    /// <summary>
    /// One independent toggle per profile. A script may belong to several profiles at
    /// once, so these are checkboxes rather than a single-selection combo box.
    /// </summary>
    public ObservableCollection<ScriptGroupToggleViewModel> Groups { get; } = new();

    /// <summary>
    /// Rebuilds the group toggles from the profile service so the UI reflects every
    /// group the script currently belongs to.
    /// </summary>
    internal void RefreshGroupMembership()
    {
        var memberNames = new HashSet<string>(
            _profiles.GetScriptProfiles(Script.Key), StringComparer.OrdinalIgnoreCase);

        Groups.Clear();
        foreach (var name in _profiles.ProfileNames)
        {
            Groups.Add(new ScriptGroupToggleViewModel(name, memberNames.Contains(name), OnGroupToggled));
        }
    }

    /// <summary>
    /// Handles a single membership edge change. Additive: assigning the script to one
    /// group never removes it from any other group.
    /// </summary>
    private void OnGroupToggled(string profileName, bool isMember)
    {
        if (isMember)
        {
            _profiles.AddScriptToProfile(Script.Key, profileName);
        }
        else
        {
            _profiles.RemoveScriptFromProfile(Script.Key, profileName);
        }
    }

    // ---- Launch on startup (daemon) -------------------------------------

    /// <summary>When true, DotAHK launches this script automatically when it boots.</summary>
    [ObservableProperty]
    private bool _launchOnStartup;

    partial void OnLaunchOnStartupChanged(bool value)
    {
        var list = _settings.Settings.AutoStartScripts;
        var present = list.Contains(Script.Key, StringComparer.OrdinalIgnoreCase);

        if (value && !present)
        {
            list.Add(Script.Key);
            _settings.Save();
        }
        else if (!value && present)
        {
            list.RemoveAll(k => string.Equals(k, Script.Key, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
        }
    }

    // ---- Process telemetry ----------------------------------------------

    /// <summary>Latest CPU usage, shown on the card (empty when the script is idle).</summary>
    [ObservableProperty]
    private string _cpuText = string.Empty;

    /// <summary>Latest working-set size, shown on the card (empty when idle).</summary>
    [ObservableProperty]
    private string _memoryText = string.Empty;

    /// <summary>True when sustained high CPU suggests a runaway (loop) script.</summary>
    [ObservableProperty]
    private bool _isHighCpu;

    /// <summary>Refreshes the CPU/memory readout from the tracker (called each second).</summary>
    public void RefreshTelemetry()
    {
        if (_tracker.TryGetSession(Script.FilePath, out var session) &&
            session is not null && !session.HasExited)
        {
            CpuText = $"{session.CpuUsagePercent:0.0}%";
            MemoryText = FormatBytes(session.WorkingSetBytes);
            IsHighCpu = session.IsHighCpu;
        }
        else
        {
            CpuText = string.Empty;
            MemoryText = string.Empty;
            IsHighCpu = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb:0.0} MB";
    }

    /// <summary>
    /// Toggles this script from a global hotkey press: stops it when running,
    /// otherwise starts it as a persistent (unmanaged) session.
    /// </summary>
    internal async Task ToggleFromHotkeyAsync()
    {
        if (_tracker.TryGetSession(Script.FilePath, out var session) &&
            session is not null && !session.HasExited)
        {
            await StopCoreAsync();
        }
        else
        {
            await StartAsync(RunMode.Persistent, null);
        }
    }

    /// <summary>Starts the script as a persistent session (used by profiles/auto-start).</summary>
    internal Task StartPersistentAsync() => StartAsync(RunMode.Persistent, null);

    /// <summary>Stops the script when it is currently running.</summary>
    internal async Task StopIfRunningAsync()
    {
        if (_tracker.TryGetSession(Script.FilePath, out var session) &&
            session is not null && !session.HasExited)
        {
            await StopCoreAsync();
        }
    }

    [ObservableProperty]
    private double _scheduleAmount = 5;

    /// <summary>0 = seconds, 1 = minutes, 2 = hours.</summary>
    [ObservableProperty]
    private int _scheduleUnitIndex = 1;

    public TimeSpan ScheduleDuration
    {
        get
        {
            var amount = Math.Max(1, (int)Math.Round(ScheduleAmount));
            return ScheduleUnitIndex switch
            {
                0 => TimeSpan.FromSeconds(amount),
                2 => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromMinutes(amount),
            };
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (_suppressToggleHandling)
        {
            return;
        }

        _ = value
            ? StartAsync(RunMode.Persistent, null)
            : StopCoreAsync();
    }

    /// <summary>Runs for the fixed 10 second burst window, then stops automatically.</summary>
    [RelayCommand]
    private Task BurstAsync() => StartAsync(RunMode.Burst, ProcessTracker.BurstDuration);

    /// <summary>Runs for the user-defined duration, then stops automatically.</summary>
    [RelayCommand]
    private Task ScheduleAsync() => StartAsync(RunMode.Scheduled, ScheduleDuration);

    /// <summary>Manually stops the script (kills only its PID).</summary>
    [RelayCommand]
    private Task Stop() => StopCoreAsync();

    /// <summary>Opens File Explorer with the script file selected.</summary>
    [RelayCommand]
    private void OpenLocation()
    {
        if (!_fileLocation.RevealInExplorer(Script.FilePath))
        {
            StatusText = LocalizationService.Get("CardLocationFailed");
        }
    }

    /// <summary>Raised when the user asks to open this script in the quick-fix editor.</summary>
    public event EventHandler<ScriptItemViewModel>? EditRequested;

    /// <summary>Raised when the user asks to hide this script (soft delete).</summary>
    public event EventHandler<ScriptItemViewModel>? HideRequested;

    /// <summary>Raised when the user asks to restore a hidden script.</summary>
    public event EventHandler<ScriptItemViewModel>? RestoreRequested;

    /// <summary>Opens the quick-fix editor for this script.</summary>
    [RelayCommand]
    private void Edit() => EditRequested?.Invoke(this, this);

    /// <summary>Hides the script from the active list (never deletes the file).</summary>
    [RelayCommand]
    private void Hide() => HideRequested?.Invoke(this, this);

    /// <summary>Restores a hidden script back into the active list.</summary>
    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, this);

    /// <summary>
    /// Stops the running script (killing only its PID) and relaunches it so the
    /// latest code on disk is loaded. When nothing was running, only the buffer is
    /// reloaded by the caller; nothing is started here.
    /// </summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        var wasRunning = _tracker.TryGetSession(Script.FilePath, out var session) && session is not null;
        var mode = session?.Mode ?? _activeMode;

        await StopCoreAsync();

        if (wasRunning)
        {
            await StartAsync(mode == RunMode.Burst ? RunMode.Burst : RunMode.Persistent, null);
        }
    }

    private async Task StartAsync(RunMode mode, TimeSpan? autoStopAfter)
    {
        if (!CanRun)
        {
            StatusText = LocalizationService.Get("CardNoAhk");
            SetActiveWithoutHandling(false);
            return;
        }

        IsBusy = true;
        try
        {
            await _tracker.StartAsync(Script, mode, autoStopAfter, Arguments);
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format("CardStartFailed", ex.Message);
            SetActiveWithoutHandling(false);
        }
        finally
        {
            IsBusy = false;
            RefreshCountdown();
        }
    }

    private async Task StopCoreAsync()
    {
        IsBusy = true;
        try
        {
            await _tracker.StopAsync(Script.FilePath);
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format("CardStopFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            RefreshCountdown();
        }
    }

    /// <summary>Reflects a started session on the UI. Only a persistent run keeps the toggle on.</summary>
    internal void ApplySessionStarted(ScriptRunSession session)
    {
        _activeMode = session.Mode;
        SetActiveWithoutHandling(session.Mode == RunMode.Persistent);

        StatusText = session.Mode switch
        {
            RunMode.Burst => LocalizationService.Format("CardBurst", session.ProcessId),
            RunMode.Scheduled => LocalizationService.Format("CardScheduled", session.ProcessId),
            _ => LocalizationService.Format("CardRunning", session.ProcessId),
        };
    }

    /// <summary>Reflects a stopped session on the UI.</summary>
    internal void ApplySessionStopped()
    {
        SetActiveWithoutHandling(false);
        CountdownText = string.Empty;
        StatusText = LocalizationService.Get("CardInactive");
    }

    /// <summary>Sets the toggle state without re-triggering start/stop logic.</summary>
    internal void SetActiveWithoutHandling(bool active)
    {
        _suppressToggleHandling = true;
        try
        {
            IsActive = active;
        }
        finally
        {
            _suppressToggleHandling = false;
        }
    }

    /// <summary>Refreshes the countdown text from the tracker (called once per second).</summary>
    public void RefreshCountdown()
    {
        if (_tracker.TryGetSession(Script.FilePath, out var session) &&
            session is not null && !session.HasExited)
        {
            var remaining = session.Remaining;
            CountdownText = remaining is { } value
                ? LocalizationService.Format(
                    "CardAutoStop",
                    value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss"))
                : string.Empty;
        }
        else
        {
            CountdownText = string.Empty;
        }
    }
}

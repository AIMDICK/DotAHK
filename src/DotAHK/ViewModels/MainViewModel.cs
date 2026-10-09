using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotAHK.Models;
using DotAHK.Services;
using Microsoft.UI.Dispatching;

namespace DotAHK.ViewModels;

/// <summary>
/// Dashboard ViewModel. Owns the scanned script list, reacts to the tracker's
/// session events, and drives a one-second ticker that refreshes countdowns.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IScriptScanner _scanner;
    private readonly IAhkInstallationService _installations;
    private readonly IProcessTracker _tracker;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly IFileLocationService _fileLocation;
    private readonly IProfileService _profiles;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly IAutoStartService _autoStart;
    private readonly DispatcherQueueTimer _ticker;

    /// <summary>Guards against piling up redundant, deferred profile refreshes.</summary>
    private bool _profileChangeQueued;

    /// <summary>True while a deferred profile refresh is being applied on the UI thread.</summary>
    private bool _isApplyingProfileChange;

    /// <summary>True while the profile ComboBox items source is being rebuilt.</summary>
    private bool _isRefreshingProfiles;

    /// <summary>Installer URL for the missing AutoHotkey v1 (legacy) runtime.</summary>
    private const string AhkV1DownloadUrl = "https://www.autohotkey.com/download/ahk-install.exe";

    /// <summary>Installer URL for the missing AutoHotkey v2 runtime.</summary>
    private const string AhkV2DownloadUrl = "https://www.autohotkey.com/download/ahk-v2.exe";

    /// <summary>
    /// Header profile-filter sentinel that disables filtering (shows all). Localized so
    /// it reads naturally; it is never persisted (the stored active profile is null).
    /// </summary>
    public static string AllScriptsOption => LocalizationService.Get("AllScriptsOption");

    /// <summary>Languages offered by the header language selector.</summary>
    public IReadOnlyList<string> Languages => LocalizationService.SupportedLanguages;

    /// <summary>Localized strings bound by the XAML (see <see cref="LocalizedStrings"/>).</summary>
    public LocalizedStrings Loc { get; } = new();

    /// <summary>
    /// Persisted UI language. Changing it saves the choice, applies the override and
    /// shows a localized restart notice (WinUI cannot re-localize already-built XAML).
    /// </summary>
    [ObservableProperty]
    private string _selectedLanguage = LocalizationService.FallbackLanguage;

    /// <summary>
    /// Applies a user-chosen language: persists it, applies the override and shows the
    /// localized restart notice. Called from the view's SelectionChanged handler so a
    /// one-way binding cannot write back spuriously during initialization.
    /// </summary>
    public void SetLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        var normalized = LocalizationService.Normalize(language);
        if (string.Equals(_settings.Settings.Language, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Settings.Language = normalized;
        _settings.Save();
        LocalizationService.Apply(normalized);
        SelectedLanguage = normalized;
        _ = ShowLanguageRestartDialogAsync();
    }

    private Task ShowLanguageRestartDialogAsync() =>
        _dialogs.AlertAsync(
            LocalizationService.Get("LanguageRestartTitle"),
            LocalizationService.Get("LanguageRestartMessage"),
            LocalizationService.Get("ConfirmOkButton"));

    public MainViewModel(
        IScriptScanner scanner,
        IAhkInstallationService installations,
        IProcessTracker tracker,
        ISettingsService settings,
        IDialogService dialogs,
        IFileLocationService fileLocation,
        IEditorService editor,
        IProfileService profiles,
        IGlobalHotkeyService hotkeys,
        IAutoStartService autoStart)
    {
        StartupTrace.Mark("MainViewModel ctor enter");
        _scanner = scanner;
        _installations = installations;
        _tracker = tracker;
        _settings = settings;
        _dialogs = dialogs;
        _fileLocation = fileLocation;
        _profiles = profiles;
        _hotkeys = hotkeys;
        _autoStart = autoStart;

        Editor = new EditorViewModel(editor, tracker, settings);
        ProfileEditor = new ProfileEditorViewModel(profiles);

        _tracker.SessionStarted += OnSessionStarted;
        _tracker.SessionStopped += OnSessionStopped;
        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _profiles.Changed += OnProfilesChanged;

        _selectedLanguage = LocalizationService.Normalize(settings.Settings.Language);
        RefreshProfiles();
        StartupTrace.Mark("MainViewModel ctor: profiles refreshed");

        _ticker = App.DispatcherQueue.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) => RefreshCountdowns();
        StartupTrace.Mark("MainViewModel ctor exit");
    }

    /// <summary>
    /// All non-hidden discovered scripts. This is the master source list and is never
    /// mutated by filtering or by profile selection.
    /// </summary>
    public ObservableCollection<ScriptItemViewModel> MasterScriptsList { get; } = new();

    /// <summary>Scripts the user soft-deleted; kept on disk and restorable.</summary>
    public ObservableCollection<ScriptItemViewModel> HiddenScripts { get; } = new();

    /// <summary>
    /// The subset of <see cref="MasterScriptsList"/> currently visible, filtered by the
    /// active profile. This is the collection the ListView binds to; it is repopulated
    /// from a snapshot of the master list so nothing is ever mutated while enumerating.
    /// </summary>
    public ObservableCollection<ScriptItemViewModel> FilteredScripts { get; } = new();

    /// <summary>Profile options for the header filter (first entry = "All Scripts").</summary>
    public ObservableCollection<string> ProfileOptions { get; } = new();

    /// <summary>The quick-fix editor overlay shared by every script row.</summary>
    public EditorViewModel Editor { get; }

    /// <summary>The profile editor overlay opened from the toolbar.</summary>
    public ProfileEditorViewModel ProfileEditor { get; }

    /// <summary>
    /// When true, new script launches inject <c>#NoTrayIcon</c> so scripts never
    /// add their own notification-area icons.
    /// </summary>
    public bool TrayHijackingEnabled
    {
        get => _settings.Settings.TrayHijackingEnabled;
        set
        {
            if (_settings.Settings.TrayHijackingEnabled == value)
            {
                return;
            }

            _settings.Settings.TrayHijackingEnabled = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True while the initial background load runs (registry probing, folder
    /// scanning, profile loading). Drives the dashboard ProgressRing so the window
    /// can paint before the work finishes.
    /// </summary>
    [ObservableProperty]
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasScripts;

    partial void OnIsInitializingChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    partial void OnHasScriptsChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    /// <summary>True only after initialization finished and no scripts were found.</summary>
    public bool ShowEmptyState => !IsInitializing && !HasScripts;

    [ObservableProperty]
    private string _statusMessage = LocalizationService.Get("StatusReady");

    /// <summary>True once at least one AutoHotkey installation was detected.</summary>
    [ObservableProperty]
    private bool _isAhkInstalled;

    /// <summary>
    /// True when the startup verification found no AutoHotkey installation, which
    /// drives the prominent InfoBar warning on the dashboard.
    /// </summary>
    [ObservableProperty]
    private bool _showAhkMissingWarning;

    /// <summary>Permanent, human-readable summary of the detected versions/status.</summary>
    [ObservableProperty]
    private string _installationStatusText = LocalizationService.Get("InstallDetecting");

    /// <summary>True when an AutoHotkey v1 interpreter was detected.</summary>
    [ObservableProperty]
    private bool _isV1Installed;

    /// <summary>True when an AutoHotkey v2 interpreter was detected.</summary>
    [ObservableProperty]
    private bool _isV2Installed;

    public string WatchFolderSummary =>
        _settings.Settings.WatchFolders.Count == 1
            ? LocalizationService.Get("WatchFolderSummaryOne")
            : LocalizationService.Format("WatchFolderSummaryMany", _settings.Settings.WatchFolders.Count);

    /// <summary>Detects installations and performs the initial scan (no prompting).</summary>
    public async Task InitializeAsync()
    {
        IsInitializing = true;
        try
        {
            await ScanAsync();
            await LaunchAutoStartScriptsAsync();
        }
        finally
        {
            // Always clear the flag so the ProgressRing disappears even on failure.
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Entry point for the Rescan button: optionally lets the user add a new watch
    /// folder, then re-detects installations and rescans every watch folder.
    /// </summary>
    [RelayCommand]
    private async Task RescanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        await PromptToAddWatchFolderAsync();
        await ScanAsync();
    }

    /// <summary>
    /// Asks whether the user wants to add a watch folder before scanning and, if so,
    /// lets them pick one. Safe to call before the initial scan too.
    /// </summary>
    private async Task PromptToAddWatchFolderAsync()
    {
        var addFolder = await _dialogs.ConfirmAsync(
            LocalizationService.Get("DialogAddWatchFolderTitle"),
            LocalizationService.Get("DialogAddWatchFolderMessage"),
            LocalizationService.Get("DialogYes"),
            LocalizationService.Get("DialogNo"));

        if (!addFolder)
        {
            return;
        }

        var folder = await _dialogs.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            AddWatchFolder(folder!);
        }
    }

    /// <summary>Re-detects installations and rescans every watch folder.</summary>
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = LocalizationService.Get("StatusScanning");
        try
        {
            // Registry detection + directory enumeration + per-file parsing all run on
            // the thread pool. The UI thread is never touched until the batch is done.
            var folders = _settings.Settings.WatchFolders.ToList();
            StartupTrace.Mark("ScanAsync: background detection + scan starting");
            var scripts = await Task.Run(async () =>
            {
                await _installations.RefreshAsync().ConfigureAwait(false);
                return await _scanner.ScanAsync(folders).ConfigureAwait(false);
            });
            StartupTrace.Mark($"ScanAsync: background work done ({scripts.Count} scripts)");

            // Marshal the finished batch onto the UI thread in a single pass.
            await ApplyScanResultAsync(scripts);
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Format("StatusScanFailed", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Applies a completed scan to the view model on the UI thread. If called from a
    /// background thread, the work is posted through <c>DispatcherQueue.TryEnqueue</c>
    /// so the observable collections are only ever mutated on the UI thread.
    /// </summary>
    private Task ApplyScanResultAsync(IReadOnlyList<AhkScript> scripts)
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            ApplyScanResult(scripts);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource();
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                ApplyScanResult(scripts);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }

    /// <summary>
    /// Applies the scan result on the UI thread as a single batch: refresh the
    /// installation status, rebuild the master list once and re-apply the filter.
    /// </summary>
    private void ApplyScanResult(IReadOnlyList<AhkScript> scripts)
    {
        UpdateInstallationStatus();
        RebuildScriptList(scripts);
        StatusMessage = scripts.Count == 1
            ? LocalizationService.Get("StatusFoundOne")
            : LocalizationService.Format("StatusFoundMany", scripts.Count);
        StartupTrace.Mark("ScanAsync: batch applied on UI thread");
    }

    /// <summary>Adds a folder to the watch list and rescans.</summary>
    public async Task AddWatchFolderAsync(string folder)
    {
        AddWatchFolder(folder);
        await ScanAsync();
    }

    /// <summary>
    /// Adds a folder to the watch list without rescanning. Returns <c>true</c> when
    /// the folder was newly added. Shared by the rescan prompt and onboarding.
    /// </summary>
    private bool AddWatchFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var folders = _settings.Settings.WatchFolders;
        if (folders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = LocalizationService.Get("StatusFolderWatched");
            return false;
        }

        folders.Add(folder);
        _settings.Save();
        OnPropertyChanged(nameof(WatchFolderSummary));
        return true;
    }

    private void RebuildScriptList(IReadOnlyList<AhkScript> scripts)
    {
        var running = _tracker.ActiveSessions
            .GroupBy(s => s.Script.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var ignored = new HashSet<string>(
            _settings.Settings.IgnoredScripts ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        MasterScriptsList.Clear();
        HiddenScripts.Clear();
        StartupTrace.Mark($"RebuildScriptList: building {scripts.Count} view models");

        foreach (var script in scripts)
        {
            var item = new ScriptItemViewModel(script, _tracker, _fileLocation, _settings, _profiles, _hotkeys);
            item.EditRequested += OnItemEditRequested;
            item.HideRequested += OnItemHideRequested;
            item.RestoreRequested += OnItemRestoreRequested;
            item.RefreshGroupMembership();

            if (running.TryGetValue(script.FilePath, out var session))
            {
                item.ApplySessionStarted(session);
                item.RefreshCountdown();
            }

            if (ignored.Contains(script.Key))
            {
                item.IsHidden = true;
                HiddenScripts.Add(item);
            }
            else
            {
                MasterScriptsList.Add(item);
            }
        }

        // Filtering also recomputes HasScripts and the hidden-count summary.
        ApplyScriptFilter();
        StartupTrace.Mark("RebuildScriptList: done");

        if ((MasterScriptsList.Count > 0 || HiddenScripts.Count > 0) && _tracker.ActiveSessions.Count > 0)
        {
            EnsureTicking();
        }
    }

    private void OnSessionStarted(object? sender, ScriptRunSession session)
    {
        RunOnUi(() =>
        {
            FindItem(session.Script.FilePath)?.ApplySessionStarted(session);
            EnsureTicking();
        });
    }

    private void OnSessionStopped(object? sender, ScriptRunSession session)
    {
        RunOnUi(() =>
        {
            FindItem(session.Script.FilePath)?.ApplySessionStopped();

            if (_tracker.ActiveSessions.Count == 0)
            {
                StopTicking();
            }
        });
    }

    private ScriptItemViewModel? FindItem(string filePath) =>
        MasterScriptsList.Concat(HiddenScripts)
            .FirstOrDefault(s => string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Opens the quick-fix editor for the script that requested it.</summary>
    private async void OnItemEditRequested(object? sender, ScriptItemViewModel item)
    {
        await Editor.OpenAsync(item.Script);
    }

    // ---- Script visibility (soft delete) --------------------------------

    /// <summary>Hidden-scripts panel visibility.</summary>
    [ObservableProperty]
    private bool _isHiddenPanelOpen;

    /// <summary>Human-readable count shown on the "Hidden" header button.</summary>
    public string HiddenScriptsSummary =>
        LocalizationService.Format("HiddenButtonCount", HiddenScripts.Count);

    /// <summary>
    /// Soft-deletes a script: stops it if running, moves it to the hidden list and
    /// persists the choice. The .ahk file itself is never touched.
    /// </summary>
    private async void OnItemHideRequested(object? sender, ScriptItemViewModel item)
    {
        await item.StopIfRunningAsync();
        MoveToHidden(item, hidden: true);
        StatusMessage = LocalizationService.Format("StatusHiddenScript", item.Name);
    }

    /// <summary>Restores a hidden script back into the active list.</summary>
    private void OnItemRestoreRequested(object? sender, ScriptItemViewModel item)
    {
        MoveToHidden(item, hidden: false);
        StatusMessage = LocalizationService.Format("StatusRestoredScript", item.Name);
    }

    /// <summary>Moves an item between the active and hidden lists and persists it.</summary>
    private void MoveToHidden(ScriptItemViewModel item, bool hidden)
    {
        var ignored = _settings.Settings.IgnoredScripts;

        if (hidden)
        {
            if (!ignored.Contains(item.Script.Key, StringComparer.OrdinalIgnoreCase))
            {
                ignored.Add(item.Script.Key);
            }

            MasterScriptsList.Remove(item);
            if (!HiddenScripts.Contains(item))
            {
                HiddenScripts.Add(item);
            }

            item.IsHidden = true;
        }
        else
        {
            ignored.RemoveAll(k => string.Equals(k, item.Script.Key, StringComparison.OrdinalIgnoreCase));
            HiddenScripts.Remove(item);
            if (!MasterScriptsList.Contains(item))
            {
                MasterScriptsList.Add(item);
            }

            item.IsHidden = false;
        }

        _settings.Save();
        ApplyScriptFilter();
    }

    [RelayCommand]
    private void ToggleHiddenPanel() => IsHiddenPanelOpen = !IsHiddenPanelOpen;

    [RelayCommand]
    private void CloseHiddenPanel() => IsHiddenPanelOpen = false;

    /// <summary>Restores every hidden script in one action.</summary>
    [RelayCommand]
    private void RestoreAllHidden()
    {
        foreach (var item in HiddenScripts.ToList())
        {
            MoveToHidden(item, hidden: false);
        }

        StatusMessage = LocalizationService.Get("StatusRestoredAll");
    }

    // ---- Global disable -------------------------------------------------

    /// <summary>
    /// Stops every tracked AutoHotkey process in one action. Each session's stop
    /// event flips its card toggle off; the loop is a defensive fallback for any
    /// session whose exit notification is delayed.
    /// </summary>
    [RelayCommand]
    private async Task DisableAllAsync()
    {
        var count = _tracker.ActiveSessions.Count;
        await _tracker.StopAllAsync();

        foreach (var item in MasterScriptsList.ToList())
        {
            item.SetActiveWithoutHandling(false);
        }

        foreach (var item in HiddenScripts.ToList())
        {
            item.SetActiveWithoutHandling(false);
        }

        StatusMessage = count switch
        {
            0 => LocalizationService.Get("StatusNoRunningScripts"),
            1 => LocalizationService.Get("StatusStoppedOne"),
            _ => LocalizationService.Format("StatusStoppedMany", count),
        };
    }

    /// <summary>Persists the editor buffer to disk.</summary>
    [RelayCommand]
    private Task SaveEditorAsync() => Editor.SaveAsync();

    /// <summary>Saves the buffer, then kills the old PID and spawns a new one.</summary>
    [RelayCommand]
    private Task SaveAndReloadEditorAsync() => Editor.SaveAndReloadAsync();

    /// <summary>Closes the editor overlay without saving.</summary>
    [RelayCommand]
    private void CloseEditor() => Editor.Close();

    /// <summary>Opens the AutoHotkey v1 installer download in the default browser.</summary>
    [RelayCommand]
    private void DownloadAhkV1() => OpenDownload(AhkV1DownloadUrl);

    /// <summary>Opens the AutoHotkey v2 installer download in the default browser.</summary>
    [RelayCommand]
    private void DownloadAhkV2() => OpenDownload(AhkV2DownloadUrl);

    private void OpenDownload(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Format("StatusCouldNotOpenDownload", ex.Message);
        }
    }

    /// <summary>
    /// Refreshes the installation summary and the "AutoHotkey missing" warning from
    /// the last detection pass. Called on the UI thread right after
    /// <c>RefreshAsync</c> so the status bar and InfoBar stay in sync with each scan.
    /// </summary>
    private void UpdateInstallationStatus()
    {
        var installations = _installations.Installations;

        // Detect the two version families independently: v1 and v2 use incompatible
        // syntax, so a given script may need either interpreter.
        IsV2Installed = installations.Any(i => i.Version == AhkVersion.V2);
        IsV1Installed = installations.Any(i => i.Version == AhkVersion.V1);
        IsAhkInstalled = IsV1Installed || IsV2Installed;

        // Warn whenever a version family is missing so its installer can be offered.
        ShowAhkMissingWarning = !IsV1Installed || !IsV2Installed;

        var parts = new List<string>();
        if (IsV2Installed)
        {
            parts.Add(FormatVersionLabel("v2", AhkVersion.V2));
        }

        if (IsV1Installed)
        {
            parts.Add(FormatVersionLabel("v1", AhkVersion.V1));
        }

        InstallationStatusText = parts.Count == 0
            ? LocalizationService.Get("InstalledNone")
            : LocalizationService.Format("InstalledList", string.Join(", ", parts));
    }

    /// <summary>Builds a "v1 (1.1.37)" style label from the detected installs.</summary>
    private string FormatVersionLabel(string label, AhkVersion version)
    {
        var display = _installations.Installations
            .Where(i => i.Version == version)
            .Select(i => i.DisplayVersion)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        return string.IsNullOrWhiteSpace(display) ? label : $"{label} ({display})";
    }

    private void EnsureTicking()
    {
        if (!_ticker.IsRunning)
        {
            _ticker.Start();
        }
    }

    private void StopTicking()
    {
        if (_ticker.IsRunning)
        {
            _ticker.Stop();
        }
    }

    private void RefreshCountdowns()
    {
        foreach (var item in MasterScriptsList.ToList())
        {
            item.RefreshCountdown();
            item.RefreshTelemetry();
        }

        // Hidden scripts may still be running; keep their countdown fresh too.
        foreach (var item in HiddenScripts)
        {
            item.RefreshCountdown();
        }
    }

    // ---- Environment profiles -------------------------------------------

    /// <summary>Currently selected header filter ("All Scripts" or a profile name).</summary>
    [ObservableProperty]
    private string _activeProfileOption = AllScriptsOption;

    partial void OnActiveProfileOptionChanged(string value)
    {
        // While the items source is rebuilt the ComboBox can transiently reset its
        // selection. Never treat that churn as a user action.
        if (_isRefreshingProfiles)
        {
            return;
        }

        // A transient null/empty must never clear the persisted active profile.
        if (string.IsNullOrWhiteSpace(value))
        {
            var fallback = string.IsNullOrEmpty(_profiles.ActiveProfileName)
                ? AllScriptsOption
                : _profiles.ActiveProfileName!;

            if (!string.Equals(_activeProfileOption, fallback, StringComparison.OrdinalIgnoreCase))
            {
                ActiveProfileOption = fallback;
            }

            return;
        }

        var showAll = string.Equals(value, AllScriptsOption, StringComparison.OrdinalIgnoreCase);
        _profiles.SetActiveProfile(showAll ? null : value);

        // Rebuild the visible list on the UI thread; the master list is never mutated.
        ApplyScriptFilter();
    }

    /// <summary>
    /// Applies the active profile filter on the UI thread. <see cref="FilteredScripts"/>
    /// is repopulated from a snapshot of <see cref="MasterScriptsList"/>; the master list
    /// itself is never touched by filtering.
    /// </summary>
    private void ApplyScriptFilter() => RunOnUi(ApplyScriptFilterCore);

    private void ApplyScriptFilterCore()
    {
        // Resolve the active profile's assigned script identifiers (file paths). A null
        // set means "no filter": the "All Scripts" default and a transient null both
        // show everything rather than emptying the list.
        HashSet<string>? memberPaths = null;
        var option = ActiveProfileOption;
        if (!string.IsNullOrWhiteSpace(option) &&
            !string.Equals(option, AllScriptsOption, StringComparison.OrdinalIgnoreCase))
        {
            memberPaths = _profiles.GetProfileScripts(option)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Snapshot the master list with ToList() so we never enumerate a collection
        // while mutating another that is bound to the UI.
        var snapshot = MasterScriptsList.ToList();
        FilteredScripts.Clear();
        foreach (var item in snapshot)
        {
            if (memberPaths is null || memberPaths.Contains(item.FilePath))
            {
                FilteredScripts.Add(item);
            }
        }

        // HasScripts tracks the VISIBLE list so the empty state appears when a
        // filter yields nothing, even though scripts still exist on disk.
        HasScripts = FilteredScripts.Count > 0;
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(HiddenScriptsSummary));
    }

    /// <summary>Title shown when the visible script list is empty.</summary>
    public string EmptyStateTitle =>
        MasterScriptsList.Count == 0
            ? LocalizationService.Get("EmptyNoScriptsTitle")
            : LocalizationService.Get("EmptyNoProfileTitle");

    /// <summary>Hint shown when the visible script list is empty.</summary>
    public string EmptyStateHint =>
        MasterScriptsList.Count == 0
            ? LocalizationService.Get("EmptyNoScriptsHint")
            : LocalizationService.Get("EmptyNoProfileHint");

    /// <summary>Creates a new environment profile from a user-supplied name.</summary>
    [RelayCommand]
    private async Task AddProfileAsync()
    {
        var name = await _dialogs.PromptAsync(
            LocalizationService.Get("DialogNewProfileTitle"),
            LocalizationService.Get("DialogNewProfileMessage"),
            LocalizationService.Get("DialogNewProfilePlaceholder"));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        StatusMessage = _profiles.AddProfile(name!)
            ? LocalizationService.Format("StatusAddedProfile", name.Trim())
            : LocalizationService.Format("StatusProfileExists", name.Trim());
    }

    /// <summary>Deletes the currently selected profile.</summary>
    [RelayCommand]
    private async Task RemoveProfileAsync()
    {
        var name = ActiveProfileOption;
        if (string.Equals(name, AllScriptsOption, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var confirm = await _dialogs.ConfirmAsync(
            LocalizationService.Get("DialogDeleteProfileTitle"),
            LocalizationService.Format("DialogDeleteProfileMessage", name),
            LocalizationService.Get("DialogDelete"),
            LocalizationService.Get("DialogCancel"));

        if (confirm && _profiles.RemoveProfile(name))
        {
            StatusMessage = LocalizationService.Format("StatusDeletedProfile", name);
        }
    }

    /// <summary>
    /// Activates the selected profile: stops every running script that is not part
    /// of the profile, then launches the profile's scripts.
    /// </summary>
    [RelayCommand]
    private async Task ActivateProfileAsync()
    {
        var option = ActiveProfileOption;
        if (string.Equals(option, AllScriptsOption, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = LocalizationService.Get("StatusSelectProfileActivate");
            return;
        }

        _profiles.SetActiveProfile(option);

        var members = _profiles.GetProfileScripts(option)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in MasterScriptsList.ToList())
        {
            if (!members.Contains(item.Script.Key))
            {
                await item.StopIfRunningAsync();
            }
        }

        var started = 0;
        foreach (var item in MasterScriptsList.ToList())
        {
            if (members.Contains(item.Script.Key))
            {
                await item.StartPersistentAsync();
                started++;
            }
        }

        StatusMessage = LocalizationService.Format("StatusProfileActive", option, started);
    }

    /// <summary>
    /// Opens the profile editor for the selected profile so its scripts can be
    /// assigned with checkboxes (one-to-many membership).
    /// </summary>
    [RelayCommand]
    private void OpenProfileEditor()
    {
        var option = ActiveProfileOption;
        if (string.Equals(option, AllScriptsOption, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = LocalizationService.Get("StatusSelectProfileEdit");
            return;
        }

        var members = _profiles.GetProfileScripts(option)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scripts = MasterScriptsList
            .Select(s => (Key: s.Script.Key, FileName: s.Name, FilePath: s.FilePath))
            .ToList();

        ProfileEditor.Load(option, scripts, members);
    }

    /// <summary>Rebuilds the profile filter options and restores the active selection.</summary>
    private void RefreshProfiles()
    {
        // Guard the ComboBox selection against the churn of rebuilding its items source.
        _isRefreshingProfiles = true;
        try
        {
            var desired = new List<string> { AllScriptsOption };
            desired.AddRange(_profiles.ProfileNames);

            for (var i = ProfileOptions.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(ProfileOptions[i], StringComparer.OrdinalIgnoreCase))
                {
                    ProfileOptions.RemoveAt(i);
                }
            }

            for (var i = 0; i < desired.Count; i++)
            {
                if (i >= ProfileOptions.Count)
                {
                    ProfileOptions.Add(desired[i]);
                }
                else if (!string.Equals(ProfileOptions[i], desired[i], StringComparison.OrdinalIgnoreCase))
                {
                    ProfileOptions[i] = desired[i];
                }
            }

            // Restore the selection from the persisted active profile; only fall back to
            // "All Scripts" when that profile genuinely no longer exists.
            var active = _profiles.ActiveProfileName;
            var target = !string.IsNullOrEmpty(active) &&
                ProfileOptions.Contains(active!, StringComparer.OrdinalIgnoreCase)
                ? active!
                : AllScriptsOption;

            if (!string.Equals(ActiveProfileOption, target, StringComparison.OrdinalIgnoreCase))
            {
                ActiveProfileOption = target;
            }
        }
        finally
        {
            _isRefreshingProfiles = false;
        }
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        // Profile changes can be raised synchronously from inside a command - for
        // example the profile editor's Save. Deferring to the dispatcher guarantees the
        // UI-bound collections are mutated only on the UI thread and never re-entrantly,
        // which previously crashed the app when assigning scripts or switching profiles.
        if (_profileChangeQueued)
        {
            return;
        }

        _profileChangeQueued = true;
        if (!App.DispatcherQueue.TryEnqueue(() =>
        {
            _profileChangeQueued = false;
            ApplyProfileChange();
        }))
        {
            _profileChangeQueued = false;
        }
    }

    private void ApplyProfileChange()
    {
        if (_isApplyingProfileChange)
        {
            return;
        }

        _isApplyingProfileChange = true;
        try
        {
            RefreshProfiles();
            foreach (var item in MasterScriptsList.ToList())
            {
                item.RefreshGroupMembership();
            }

            // Membership changed: re-apply the visual filter so it stays accurate.
            ApplyScriptFilterCore();
        }
        finally
        {
            _isApplyingProfileChange = false;
        }
    }

    // ---- Daemon / auto-start --------------------------------------------

    /// <summary>When true, Windows launches DotAHK automatically at sign-in.</summary>
    public bool LaunchOnStartup
    {
        get => _settings.Settings.LaunchOnStartup;
        set
        {
            if (_settings.Settings.LaunchOnStartup == value)
            {
                return;
            }

            if (!_autoStart.SetEnabled(value))
            {
                StatusMessage = value
                    ? LocalizationService.Get("StatusCouldNotEnableStartup")
                    : LocalizationService.Get("StatusCouldNotDisableStartup");
                return;
            }

            _settings.Settings.LaunchOnStartup = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Launches every script flagged "Launch on startup" that is not already running.</summary>
    public async Task LaunchAutoStartScriptsAsync()
    {
        var keys = new HashSet<string>(_settings.Settings.AutoStartScripts, StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
        {
            return;
        }

        var launched = 0;
        foreach (var item in MasterScriptsList.ToList())
        {
            if (!keys.Contains(item.Script.Key))
            {
                continue;
            }

            if (_tracker.TryGetSession(item.FilePath, out var session) && session is not null && !session.HasExited)
            {
                continue;
            }

            await item.StartPersistentAsync();
            launched++;
        }

        if (launched > 0)
        {
            StatusMessage = LocalizationService.Format("StatusAutoStarted", launched);
        }
    }

    private void OnHotkeyPressed(object? sender, string scriptKey) => RunOnUi(() =>
    {
        var item = MasterScriptsList.Concat(HiddenScripts).FirstOrDefault(
            s => string.Equals(s.Script.Key, scriptKey, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            _ = item.ToggleFromHotkeyAsync();
        }
    });

    /// <summary>Marshals an action onto the UI thread (tracker events may be raised off it).</summary>
    private static void RunOnUi(Action action)
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            dispatcher.TryEnqueue(() => action());
        }
    }
}

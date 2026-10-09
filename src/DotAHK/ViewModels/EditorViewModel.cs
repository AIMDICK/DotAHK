using CommunityToolkit.Mvvm.ComponentModel;
using DotAHK.Models;
using DotAHK.Services;

namespace DotAHK.ViewModels;

/// <summary>
/// Backs the in-app quick-fix editor overlay. Loads a single script into a text
/// buffer, tracks the dirty state, and exposes save and hot-reload operations.
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    private readonly IEditorService _editor;
    private readonly IProcessTracker _tracker;
    private readonly ISettingsService _settings;
    private AhkScript? _script;

    public EditorViewModel(IEditorService editor, IProcessTracker tracker, ISettingsService settings)
    {
        _editor = editor;
        _tracker = tracker;
        _settings = settings;
    }

    /// <summary>Whether the editor overlay is currently shown.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>File name (with extension) of the script being edited.</summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    /// <summary>Absolute path of the script being edited.</summary>
    [ObservableProperty]
    private string _filePath = string.Empty;

    /// <summary>The editable script text.</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>True when the buffer differs from the last saved content.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>True while a save or hot-reload is in progress.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Human readable result of the last operation.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnTextChanged(string value) => IsDirty = true;

    /// <summary>Loads the script's raw text and shows the editor.</summary>
    public async Task OpenAsync(AhkScript script)
    {
        _script = script;
        FileName = script.FileName;
        FilePath = script.FilePath;

        try
        {
            Text = await _editor.ReadAsync(script.FilePath);
            StatusMessage = LocalizationService.Get("EditorReady");
        }
        catch (Exception ex)
        {
            Text = string.Empty;
            StatusMessage = LocalizationService.Format("EditorCouldNotRead", ex.Message);
        }

        IsDirty = false;
        IsOpen = true;
    }

    /// <summary>Writes the buffer back to disk.</summary>
    public async Task SaveAsync()
    {
        if (_script is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _editor.SaveAsync(_script.FilePath, Text);
            IsDirty = false;
            StatusMessage = LocalizationService.Format("EditorSavedAt", DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Format("EditorSaveFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Saves the buffer, kills the currently tracked PID, then launches a fresh
    /// process so the running script picks up the new code immediately.
    /// </summary>
    public async Task SaveAndReloadAsync()
    {
        if (_script is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Capture the running mode before stopping so the reload preserves it.
            var mode = _tracker.TryGetSession(_script.FilePath, out var session) && session is not null
                ? session.Mode
                : RunMode.Persistent;

            await _editor.SaveAsync(_script.FilePath, Text);
            IsDirty = false;

            var arguments = _settings.Settings.ScriptArguments.TryGetValue(_script.Key, out var stored)
                ? stored
                : null;

            // Kill only the old PID, then spawn a brand-new process.
            await _tracker.StopAsync(_script.FilePath);

            var reloadMode = mode == RunMode.Burst ? RunMode.Burst : RunMode.Persistent;
            await _tracker.StartAsync(_script, reloadMode, autoStopAfter: null, arguments: arguments);

            StatusMessage = LocalizationService.Format("EditorHotReloadedAt", DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception ex)
        {
            StatusMessage = LocalizationService.Format("EditorHotReloadFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Closes the editor without saving.</summary>
    public void Close()
    {
        IsOpen = false;
        _script = null;
        Text = string.Empty;
        FileName = string.Empty;
        FilePath = string.Empty;
        StatusMessage = string.Empty;
        IsDirty = false;
    }
}

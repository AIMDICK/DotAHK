using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotAHK.Services;

namespace DotAHK.ViewModels;

/// <summary>
/// Backs the profile editor overlay. It lists every discovered script with a
/// checkbox so the user can choose the membership of one environment profile in a
/// single pass (one-to-many: a script belongs to at most one profile).
/// </summary>
public partial class ProfileEditorViewModel : ObservableObject
{
    private readonly IProfileService _profiles;

    public ProfileEditorViewModel(IProfileService profiles)
    {
        _profiles = profiles;
    }

    /// <summary>One row per discovered script, with its checked state.</summary>
    public ObservableCollection<ProfileScriptSelectionViewModel> Scripts { get; } = new();

    /// <summary>Whether the overlay is currently visible.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Name of the profile being edited.</summary>
    [ObservableProperty]
    private string _profileName = string.Empty;

    /// <summary>Human readable result of the last operation.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Fills the list with the supplied scripts, checking the ones that already
    /// belong to the profile, and shows the overlay.
    /// </summary>
    public void Load(
        string profileName,
        IEnumerable<(string Key, string FileName, string FilePath)> scripts,
        ISet<string> memberKeys)
    {
        ProfileName = profileName;
        Scripts.Clear();

        foreach (var (key, fileName, filePath) in scripts)
        {
            Scripts.Add(new ProfileScriptSelectionViewModel(
                key, fileName, filePath, memberKeys.Contains(key)));
        }

        StatusMessage = LocalizationService.Format("ProfileEditorScriptsAvailable", Scripts.Count);
        IsOpen = true;
    }

    /// <summary>Persists the current selection as the profile's membership.</summary>
    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            Close();
            return;
        }

        var selected = Scripts.Where(s => s.IsSelected).Select(s => s.ScriptKey).ToList();
        _profiles.SetProfileScripts(ProfileName, selected);
        StatusMessage = LocalizationService.Get("ProfileEditorSaved");
        Close();
    }

    /// <summary>Closes the overlay without saving.</summary>
    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Scripts.Clear();
        ProfileName = string.Empty;
        StatusMessage = string.Empty;
    }
}

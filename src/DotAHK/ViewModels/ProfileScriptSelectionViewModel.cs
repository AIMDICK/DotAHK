using CommunityToolkit.Mvvm.ComponentModel;

namespace DotAHK.ViewModels;

/// <summary>
/// A single selectable script row inside the profile editor overlay. The check
/// state represents membership of the profile currently being edited.
/// </summary>
public partial class ProfileScriptSelectionViewModel : ObservableObject
{
    public ProfileScriptSelectionViewModel(string scriptKey, string fileName, string filePath, bool isSelected)
    {
        ScriptKey = scriptKey;
        FileName = fileName;
        FilePath = filePath;
        _isSelected = isSelected;
    }

    /// <summary>Normalized script key (matches <c>AhkScript.Key</c>).</summary>
    public string ScriptKey { get; }

    /// <summary>File name including extension.</summary>
    public string FileName { get; }

    /// <summary>Absolute path of the script.</summary>
    public string FilePath { get; }

    /// <summary>True when the script belongs to the profile being edited.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

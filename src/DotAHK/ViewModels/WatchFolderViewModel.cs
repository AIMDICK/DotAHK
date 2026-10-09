using CommunityToolkit.Mvvm.ComponentModel;

namespace DotAHK.ViewModels;

/// <summary>Represents a single candidate/active watch folder with a check box.</summary>
public partial class WatchFolderViewModel : ObservableObject
{
    public WatchFolderViewModel(string path)
    {
        Path = path;
    }

    public string Path { get; }

    [ObservableProperty]
    private bool _isSelected = true;
}

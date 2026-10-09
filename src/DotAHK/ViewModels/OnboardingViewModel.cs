using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DotAHK.Services;

namespace DotAHK.ViewModels;

/// <summary>
/// Backs the first-run folder-picker page. Defaults to My Documents and Desktop,
/// lets the user toggle folders or add one manually, then persists the selection.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    public OnboardingViewModel(ISettingsService settings)
    {
        _settings = settings;

        var seed = settings.Settings.WatchFolders.Count > 0
            ? settings.Settings.WatchFolders
            : GetDefaultFolders();

        foreach (var folder in seed)
        {
            AddFolder(folder);
        }
    }

    public ObservableCollection<WatchFolderViewModel> Folders { get; } = new();

    /// <summary>Localized text surface for the XAML bindings (x:Uid cannot follow a runtime override).</summary>
    public LocalizedStrings Loc { get; } = new();

    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Folders.Add(new WatchFolderViewModel(path));
    }

    public IReadOnlyList<string> GetSelectedFolders() =>
        Folders.Where(f => f.IsSelected).Select(f => f.Path).ToList();

    /// <summary>Persists the chosen folders and marks onboarding as complete.</summary>
    public void Complete()
    {
        _settings.Settings.WatchFolders = GetSelectedFolders().ToList();
        _settings.Settings.HasCompletedOnboarding = true;
        _settings.Save();
    }

    private static IEnumerable<string> GetDefaultFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }
}

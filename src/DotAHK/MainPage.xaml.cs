using DotAHK.Services;
using DotAHK.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DotAHK;

/// <summary>
/// The dashboard page: lists discovered scripts and runs the initial scan.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Guards the language ComboBox against writing back the persisted language while
    /// its initial selection is being applied.
    /// </summary>
    private bool _suppressLanguageChange;

    public MainPage()
    {
        ViewModel = AppServices.CreateMainViewModel();
        InitializeComponent();
        InitializeLanguageSelection();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Selects the item whose <c>Tag</c> matches the currently applied culture code
    /// without treating the programmatic selection as a user action.
    /// </summary>
    private void InitializeLanguageSelection()
    {
        _suppressLanguageChange = true;
        try
        {
            var current = LocalizationService.CurrentLanguage;
            ComboBoxItem? match = null;

            foreach (var item in LanguageCombo.Items)
            {
                if (item is ComboBoxItem candidate &&
                    string.Equals(candidate.Tag as string, current, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                    break;
                }
            }

            LanguageCombo.SelectedItem = match ?? LanguageCombo.Items.FirstOrDefault();
        }
        finally
        {
            _suppressLanguageChange = false;
        }
    }

    /// <summary>
    /// Applies the culture code carried in the selected item's <c>Tag</c> (never its
    /// display text). Kept explicit rather than a two-way binding so the ComboBox cannot
    /// rewrite the persisted language while it initializes its items.
    /// </summary>
    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange)
        {
            return;
        }

        if (sender is ComboBox combo &&
            combo.SelectedItem is ComboBoxItem item &&
            item.Tag is string cultureCode)
        {
            ViewModel.SetLanguage(cultureCode);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
        StartupTrace.Mark("MainPage initialization complete");
    }
}

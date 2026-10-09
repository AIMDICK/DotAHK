using DotAHK.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DotAHK;

/// <summary>
/// First-run page that lets the user pick the folders to scan for scripts.
/// </summary>
public sealed partial class OnboardingPage : Page
{
    public OnboardingViewModel ViewModel { get; }

    public OnboardingPage()
    {
        ViewModel = AppServices.CreateOnboardingViewModel();
        InitializeComponent();
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");

        // Unpackaged apps must associate the picker with the window handle explicitly.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddFolder(folder.Path);
        }
    }

    private void OnGetStartedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Complete();
        Frame.Navigate(typeof(MainPage));
    }
}

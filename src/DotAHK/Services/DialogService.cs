using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IDialogService"/>. Every dialog/picker is created and shown
/// on the UI thread with the window's <see cref="XamlRoot"/>, which is required by
/// this unpackaged WinUI app.
/// </summary>
public sealed class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string yesButtonText = "Yes",
        string noButtonText = "No")
    {
        return RunOnUiAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = yesButtonText,
                CloseButtonText = noButtonText,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = GetXamlRoot(),
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        });
    }

    public Task<string?> PickFolderAsync()
    {
        return RunOnUiAsync(async () =>
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };

            // WinUI requires at least one filter entry even for a folder picker.
            picker.FileTypeFilter.Add("*");

            // Unpackaged apps must associate the picker with the window handle.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        });
    }

    public Task<string?> PromptAsync(string title, string message, string placeholder = "")
    {
        return RunOnUiAsync(async () =>
        {
            var input = new TextBox
            {
                PlaceholderText = placeholder,
                Width = 320,
                SelectionStart = 0,
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(input);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = GetXamlRoot(),
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var text = input.Text?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        });
    }

    public Task AlertAsync(string title, string message, string closeButtonText)
    {
        return RunOnUiAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = closeButtonText,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = GetXamlRoot(),
            };

            await dialog.ShowAsync();
            return true;
        });
    }

    private static XamlRoot GetXamlRoot()
    {
        if (App.Window.Content is FrameworkElement element && element.XamlRoot is { } root)
        {
            return root;
        }

        throw new InvalidOperationException("The application window is not ready to show dialogs.");
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and returns its result.</summary>
    private static async Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            return await action();
        }

        var tcs = new TaskCompletionSource<T>();
        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                tcs.TrySetResult(await action());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException("Unable to reach the UI thread."));
        }

        return await tcs.Task;
    }
}

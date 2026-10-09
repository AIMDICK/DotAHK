namespace DotAHK.Services;

/// <summary>
/// Abstraction over the modal WinUI dialogs and shell pickers so view models can
/// prompt the user without referencing XAML types directly. Implementations are
/// responsible for marshaling every call onto the UI thread.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a modal Yes/No dialog and returns true when the user chooses "Yes".
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Body text.</param>
    /// <param name="yesButtonText">Text for the affirmative (primary) button.</param>
    /// <param name="noButtonText">Text for the dismissive (close) button.</param>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string yesButtonText = "Yes",
        string noButtonText = "No");

    /// <summary>
    /// Shows the folder picker. Returns the selected absolute path, or null when
    /// the user cancels.
    /// </summary>
    Task<string?> PickFolderAsync();

    /// <summary>
    /// Shows a single-line text input dialog. Returns the trimmed text, or null
    /// when the user cancels or submits an empty value.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Body/instruction text.</param>
    /// <param name="placeholder">Optional placeholder shown in the text box.</param>
    Task<string?> PromptAsync(string title, string message, string placeholder = "");

    /// <summary>
    /// Shows a simple informational dialog with a single dismiss button. Used for the
    /// language-restart notice.
    /// </summary>
    Task AlertAsync(string title, string message, string closeButtonText);
}

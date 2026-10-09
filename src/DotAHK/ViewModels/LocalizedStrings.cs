using DotAHK.Services;

namespace DotAHK.ViewModels;

/// <summary>
/// Strongly-typed localization surface for XAML. Because an unpackaged WinUI app does
/// NOT honor a runtime language override for <c>x:Uid</c> (it always resolves against the
/// OS language), the UI text is bound through this provider, which reads the chosen
/// language via <see cref="LocalizationService"/> (MRT Core with a pinned context).
/// Values are stable for the lifetime of the window; changing language restarts the app.
/// </summary>
public sealed class LocalizedStrings
{
    public string ScriptsHeaderTitle => LocalizationService.Get("ScriptsHeaderTitle");

    public string TrayIconsToggleOff => LocalizationService.Get("TrayIconsToggle.OffContent");
    public string TrayIconsToggleOn => LocalizationService.Get("TrayIconsToggle.OnContent");
    public string TrayIconsToggleTip => LocalizationService.Get("TrayIconsToggle.ToolTipService.ToolTip");

    public string DisableAllText => LocalizationService.Get("DisableAllText.Text");
    public string DisableAllTip => LocalizationService.Get("DisableAllButton.ToolTipService.ToolTip");
    public string HiddenButtonTip => LocalizationService.Get("HiddenButton.ToolTipService.ToolTip");
    public string RescanText => LocalizationService.Get("RescanText.Text");
    public string LanguageComboTip => LocalizationService.Get("LanguageCombo.ToolTipService.ToolTip");

    public string ProfileComboTip => LocalizationService.Get("ProfileCombo.ToolTipService.ToolTip");
    public string ActivateText => LocalizationService.Get("ActivateText.Text");
    public string ActivateTip => LocalizationService.Get("ActivateButton.ToolTipService.ToolTip");
    public string AddProfileTip => LocalizationService.Get("AddProfileButton.ToolTipService.ToolTip");
    public string RemoveProfileTip => LocalizationService.Get("RemoveProfileButton.ToolTipService.ToolTip");
    public string EditProfileTip => LocalizationService.Get("EditProfileButton.ToolTipService.ToolTip");

    public string StartupToggleOff => LocalizationService.Get("StartupToggle.OffContent");
    public string StartupToggleOn => LocalizationService.Get("StartupToggle.OnContent");
    public string StartupToggleTip => LocalizationService.Get("StartupToggle.ToolTipService.ToolTip");

    public string OpenLocationTip => LocalizationService.Get("OpenLocationButton.ToolTipService.ToolTip");
    public string HighCpuText => LocalizationService.Get("HighCpuText.Text");

    public string BurstText => LocalizationService.Get("BurstText.Text");
    public string BurstTip => LocalizationService.Get("BurstButton.ToolTipService.ToolTip");
    public string EditTip => LocalizationService.Get("EditButton.ToolTipService.ToolTip");
    public string ReloadTip => LocalizationService.Get("ReloadButton.ToolTipService.ToolTip");
    public string HideTip => LocalizationService.Get("HideButton.ToolTipService.ToolTip");

    public string ActiveToggleOff => LocalizationService.Get("ActiveToggle.OffContent");
    public string ActiveToggleOn => LocalizationService.Get("ActiveToggle.OnContent");
    public string ActiveToggleTip => LocalizationService.Get("ActiveToggle.ToolTipService.ToolTip");

    public string AdvancedHeader => LocalizationService.Get("AdvancedExpander.Header");
    public string RunForText => LocalizationService.Get("RunForText.Text");
    public string SecondsItem => LocalizationService.Get("SecondsItem.Content");
    public string MinutesItem => LocalizationService.Get("MinutesItem.Content");
    public string HoursItem => LocalizationService.Get("HoursItem.Content");
    public string ScheduleButton => LocalizationService.Get("ScheduleButton.Content");
    public string ScheduleTip => LocalizationService.Get("ScheduleButton.ToolTipService.ToolTip");
    public string ArgumentsLabel => LocalizationService.Get("ArgumentsLabel.Text");
    public string ArgumentsPlaceholder => LocalizationService.Get("ArgumentsBox.PlaceholderText");
    public string ArgumentsTip => LocalizationService.Get("ArgumentsBox.ToolTipService.ToolTip");
    public string HotkeyLabel => LocalizationService.Get("HotkeyLabel.Text");
    public string HotkeyPlaceholder => LocalizationService.Get("HotkeyBox.PlaceholderText");
    public string HotkeyTip => LocalizationService.Get("HotkeyBox.ToolTipService.ToolTip");
    public string InScriptText => LocalizationService.Get("InScriptText.Text");
    public string GroupsLabel => LocalizationService.Get("GroupsLabel.Text");
    public string GroupToggleTip => LocalizationService.Get("GroupToggle.ToolTipService.ToolTip");
    public string LaunchOnStartup => LocalizationService.Get("LaunchOnStartupCheck.Content");
    public string LaunchOnStartupTip => LocalizationService.Get("LaunchOnStartupCheck.ToolTipService.ToolTip");

    public string DownloadV1Text => LocalizationService.Get("DownloadV1Text.Text");
    public string DownloadV1Tip => LocalizationService.Get("DownloadV1Button.ToolTipService.ToolTip");
    public string DownloadV2Text => LocalizationService.Get("DownloadV2Text.Text");
    public string DownloadV2Tip => LocalizationService.Get("DownloadV2Button.ToolTipService.ToolTip");

    public string QuickFixEditorTitle => LocalizationService.Get("QuickFixEditorTitle.Text");
    public string EditorCloseButton => LocalizationService.Get("EditorCloseButton.Content");
    public string EditorSaveButton => LocalizationService.Get("EditorSaveButton.Content");
    public string EditorSaveReloadButton => LocalizationService.Get("EditorSaveReloadButton.Content");
    public string AssignProfileTitle => LocalizationService.Get("AssignProfileTitle.Text");
    public string ProfileEditorCloseButton => LocalizationService.Get("ProfileEditorCloseButton.Content");
    public string ProfileEditorSaveButton => LocalizationService.Get("ProfileEditorSaveButton.Content");
    public string HiddenScriptsTitle => LocalizationService.Get("HiddenScriptsTitle.Text");
    public string HiddenScriptsHint => LocalizationService.Get("HiddenScriptsHint.Text");
    public string RestoreText => LocalizationService.Get("RestoreText.Text");
    public string RestoreTip => LocalizationService.Get("RestoreButton.ToolTipService.ToolTip");
    public string RestoreAllButton => LocalizationService.Get("RestoreAllButton.Content");
    public string HiddenCloseButton => LocalizationService.Get("HiddenCloseButton.Content");

    // First-run onboarding page.
    public string WelcomeTitle => LocalizationService.Get("WelcomeTitle.Text");
    public string WelcomeBody => LocalizationService.Get("WelcomeBody.Text");
    public string AddFolderText => LocalizationService.Get("AddFolderText.Text");
    public string GetStartedButton => LocalizationService.Get("GetStartedButton.Content");

    // Splash / loading page.
    public string LoadingText => LocalizationService.Get("LoadingText.Text");
}

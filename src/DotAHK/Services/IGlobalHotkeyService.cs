using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Registers system-wide hotkeys through Win32 <c>RegisterHotKey</c> so an
/// interpreter can be toggled even when DotAHK is not the foreground window.
/// </summary>
public interface IGlobalHotkeyService
{
    /// <summary>
    /// Raised on the UI thread when a registered hotkey fires. The argument is the
    /// normalized script path (<see cref="AhkScript.Key"/>) the hotkey belongs to.
    /// </summary>
    event EventHandler<string>? HotkeyPressed;

    /// <summary>
    /// Registers (or re-registers) a hotkey for a script. Any previous binding for
    /// the same script is replaced. When registration fails (for example the
    /// combination is already owned by another application),
    /// <paramref name="error"/> describes the reason and false is returned.
    /// </summary>
    bool TryRegister(string scriptKey, HotkeyGesture gesture, out string? error);

    /// <summary>Removes the hotkey bound to a script, if any.</summary>
    void Unregister(string scriptKey);

    /// <summary>Removes every hotkey registered by this service.</summary>
    void UnregisterAll();
}

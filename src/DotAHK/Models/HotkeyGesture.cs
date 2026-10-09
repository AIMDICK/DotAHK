namespace DotAHK.Models;

/// <summary>
/// A system-wide hotkey gesture (a set of modifier keys plus a main key) captured
/// from the user. The raw modifier flags are kept here so the global hotkey
/// service can pass them straight to <c>RegisterHotKey</c>.
/// </summary>
public sealed class HotkeyGesture
{
    public bool Control { get; init; }

    public bool Alt { get; init; }

    public bool Shift { get; init; }

    public bool Windows { get; init; }

    /// <summary>Win32 virtual-key code of the main (non-modifier) key.</summary>
    public int VirtualKey { get; init; }

    /// <summary>Human readable form, for example "Ctrl+Alt+G".</summary>
    public string DisplayText { get; init; } = string.Empty;

    /// <summary>
    /// Win32 modifier bit flags (MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4,
    /// MOD_WIN = 8). Combined by the caller with <c>RegisterHotKey</c>'s
    /// <c>MOD_NOREPEAT</c> flag.
    /// </summary>
    public int ModifierFlags
    {
        get
        {
            var flags = 0;
            if (Alt)
            {
                flags |= 0x0001;
            }

            if (Control)
            {
                flags |= 0x0002;
            }

            if (Shift)
            {
                flags |= 0x0004;
            }

            if (Windows)
            {
                flags |= 0x0008;
            }

            return flags;
        }
    }

    /// <summary>True when at least one modifier is held (a bare key is rejected).</summary>
    public bool HasModifier => Control || Alt || Shift || Windows;

    public override string ToString() => DisplayText;
}

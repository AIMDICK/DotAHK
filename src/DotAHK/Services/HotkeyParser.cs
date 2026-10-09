using System.IO;
using System.Text.RegularExpressions;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Extracts native AutoHotkey hotkey definitions from a script and converts
/// between the user's "Ctrl+Alt+G" text form and a <see cref="HotkeyGesture"/>.
/// The AHK extraction is a best-effort textual scan: it recognises the classic
/// <c>^!a::</c> / <c>F1::</c> forms that start a hotkey definition line.
/// </summary>
public static class HotkeyParser
{
    /// <summary>Maximum number of source lines inspected per script (keeps scans fast).</summary>
    private const int MaxLines = 20000;

    /// <summary>Maximum number of distinct hotkeys surfaced for a single script.</summary>
    private const int MaxHotkeys = 12;

    // A hotkey definition starts at the beginning of a (non-comment) line with a
    // run of modifier/prefix symbols and key characters followed by "::".
    private static readonly Regex HotkeyDefinitionRegex = new(
        @"^\s*(?<hotkey>[!^#+~*$<>&\w\-`]+)\s*::",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Reads <paramref name="filePath"/> and returns the distinct AHK hotkeys it
    /// defines, in source order. Returns an empty list when the file cannot be
    /// read or contains no hotkeys.
    /// </summary>
    public static IReadOnlyList<string> ExtractHotkeys(string filePath)
    {
        var results = new List<string>();
        try
        {
            using var reader = new StreamReader(filePath);
            var inBlockComment = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < MaxLines && reader.ReadLine() is { } line; i++)
            {
                var trimmed = line.Trim();

                // Track /* ... */ block comments so hotkeys inside them are ignored.
                if (inBlockComment)
                {
                    if (trimmed.Contains("*/", StringComparison.Ordinal))
                    {
                        inBlockComment = false;
                    }

                    continue;
                }

                if (trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    inBlockComment = !trimmed.Contains("*/", StringComparison.Ordinal);
                    continue;
                }

                if (trimmed.Length == 0 ||
                    trimmed.StartsWith(';') ||
                    trimmed.StartsWith("::", StringComparison.Ordinal))
                {
                    // Blank line, line comment, or an auto-execute/continuation marker.
                    continue;
                }

                var match = HotkeyDefinitionRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var hotkey = match.Groups["hotkey"].Value.Trim();
                if (hotkey.Length > 0 && seen.Add(hotkey))
                {
                    results.Add(hotkey);
                    if (results.Count >= MaxHotkeys)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A single unreadable script must never abort the scan.
        }

        return results;
    }

    /// <summary>Formats a gesture in the canonical "Ctrl+Alt+G" order.</summary>
    public static string Format(bool control, bool alt, bool shift, bool windows, int virtualKey)
    {
        var parts = new List<string>(5);
        if (control)
        {
            parts.Add("Ctrl");
        }

        if (alt)
        {
            parts.Add("Alt");
        }

        if (shift)
        {
            parts.Add("Shift");
        }

        if (windows)
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(virtualKey));
        return string.Join('+', parts);
    }

    /// <summary>
    /// Parses a "Ctrl+Alt+G" style string. Returns false when the text is empty,
    /// has no recognisable main key, or carries no modifier (a bare key would be
    /// far too aggressive as a global hotkey).
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var control = false;
        var alt = false;
        var shift = false;
        var windows = false;
        var virtualKey = 0;

        foreach (var token in tokens)
        {
            if (IsModifier(token, "ctrl", "control"))
            {
                control = true;
            }
            else if (IsModifier(token, "alt"))
            {
                alt = true;
            }
            else if (IsModifier(token, "shift"))
            {
                shift = true;
            }
            else if (IsModifier(token, "win", "windows"))
            {
                windows = true;
            }
            else
            {
                var key = ParseKeyName(token);
                if (key <= 0)
                {
                    return false;
                }

                virtualKey = key;
            }
        }

        if (virtualKey <= 0)
        {
            return false;
        }

        if (!(control || alt || shift || windows))
        {
            return false;
        }

        gesture = new HotkeyGesture
        {
            Control = control,
            Alt = alt,
            Shift = shift,
            Windows = windows,
            VirtualKey = virtualKey,
            DisplayText = Format(control, alt, shift, windows, virtualKey),
        };
        return true;
    }

    private static bool IsModifier(string token, params string[] names) =>
        names.Any(name => string.Equals(token, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Maps a Win32 virtual-key code to its display name.</summary>
    public static string KeyName(int virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z' || virtualKey is >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return "F" + (virtualKey - 0x70 + 1);
        }

        return virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x60 => "Num0",
            0x61 => "Num1",
            0x62 => "Num2",
            0x63 => "Num3",
            0x64 => "Num4",
            0x65 => "Num5",
            0x66 => "Num6",
            0x67 => "Num7",
            0x68 => "Num8",
            0x69 => "Num9",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "Key" + virtualKey,
        };
    }

    /// <summary>Maps a display name back to a Win32 virtual-key code (0 when unknown).</summary>
    public static int ParseKeyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        var token = name.Trim();

        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return ch;
            }
        }

        if ((token.StartsWith('F') || token.StartsWith('f')) &&
            int.TryParse(token.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            return 0x70 + (fn - 1);
        }

        return token.ToUpperInvariant() switch
        {
            "BACKSPACE" => 0x08,
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" or "SPACEBAR" => 0x20,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "END" => 0x23,
            "HOME" => 0x24,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "INSERT" or "INS" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "NUM0" or "NUMPAD0" => 0x60,
            "NUM1" or "NUMPAD1" => 0x61,
            "NUM2" or "NUMPAD2" => 0x62,
            "NUM3" or "NUMPAD3" => 0x63,
            "NUM4" or "NUMPAD4" => 0x64,
            "NUM5" or "NUMPAD5" => 0x65,
            "NUM6" or "NUMPAD6" => 0x66,
            "NUM7" or "NUMPAD7" => 0x67,
            "NUM8" or "NUMPAD8" => 0x68,
            "NUM9" or "NUMPAD9" => 0x69,
            "NUM*" or "NUMPADMULT" => 0x6A,
            "NUM+" or "NUMPADADD" => 0x6B,
            "NUM-" or "NUMPADSUB" => 0x6D,
            "NUM." or "NUMPADDECIMAL" => 0x6E,
            "NUM/" or "NUMPADDIV" => 0x6F,
            ";" => 0xBA,
            "=" => 0xBB,
            "," => 0xBC,
            "-" => 0xBD,
            "." => 0xBE,
            "/" => 0xBF,
            "`" => 0xC0,
            "[" => 0xDB,
            "\\" => 0xDC,
            "]" => 0xDD,
            "'" => 0xDE,
            _ => 0,
        };
    }
}

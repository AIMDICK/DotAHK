namespace DotAHK.Models;

/// <summary>
/// The major version family of an AutoHotkey interpreter.
/// AutoHotkey v1 and v2 use incompatible script syntax, so the launcher must
/// pick the correct interpreter for a given script.
/// </summary>
public enum AhkVersion
{
    /// <summary>The version could not be determined.</summary>
    Unknown = 0,

    /// <summary>AutoHotkey 1.0 - 1.1.x.</summary>
    V1 = 1,

    /// <summary>AutoHotkey 2.x.</summary>
    V2 = 2,
}

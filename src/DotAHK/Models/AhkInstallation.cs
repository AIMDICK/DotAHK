namespace DotAHK.Models;

/// <summary>
/// Describes a single AutoHotkey installation detected on the machine
/// (for example the one registered in the registry under
/// <c>HKEY_LOCAL_MACHINE\SOFTWARE\AutoHotkey</c>).
/// </summary>
public sealed class AhkInstallation
{
    /// <summary>Detected version family (v1 or v2).</summary>
    public AhkVersion Version { get; init; } = AhkVersion.Unknown;

    /// <summary>
    /// Full path to the interpreter executable chosen for this installation.
    /// The architecture-specific binary is preferred over the generic launcher
    /// so the tracked PID is the real interpreter process.
    /// </summary>
    public string InterpreterPath { get; init; } = string.Empty;

    /// <summary>Root install directory (for example "C:\Program Files\AutoHotkey").</summary>
    public string InstallDirectory { get; init; } = string.Empty;

    /// <summary>Human readable version string (for example "2.0.26").</summary>
    public string? DisplayVersion { get; init; }

    public override string ToString() =>
        $"AutoHotkey {DisplayVersion ?? Version.ToString()} ({InterpreterPath})";
}

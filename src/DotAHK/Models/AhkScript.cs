using System.IO;

namespace DotAHK.Models;

/// <summary>
/// Represents a single AutoHotkey script file discovered on disk.
/// </summary>
public sealed class AhkScript
{
    public AhkScript(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>Absolute path to the .ahk/.ahk1/.ahk2 file.</summary>
    public string FilePath { get; }

    /// <summary>File name including extension.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Directory that contains the script.</summary>
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public long FileSizeBytes { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    /// <summary>Version detected by inspecting the script's <c>#Requires</c> directive.</summary>
    public AhkVersion DetectedVersion { get; set; } = AhkVersion.Unknown;

    /// <summary>Installation resolved to launch this script (null when none could be resolved).</summary>
    public AhkInstallation? Installation { get; set; }

    /// <summary>
    /// Native AHK hotkeys detected in the script's source (for example "^!a" or
    /// "F1"). Displayed as read-only tags on the script card.
    /// </summary>
    public List<string> DetectedHotkeys { get; } = new();

    /// <summary>
    /// Case-insensitive identity used as a dictionary key and for change detection.
    /// </summary>
    public string Key => FilePath.ToLowerInvariant();
}

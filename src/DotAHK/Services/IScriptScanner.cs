using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Scans a set of folders for AutoHotkey script files (.ahk, .ahk1, .ahk2).
/// </summary>
public interface IScriptScanner
{
    /// <summary>
    /// Scans the supplied folders (recursively) and returns the discovered
    /// scripts. The operation runs on a background thread.
    /// </summary>
    Task<IReadOnlyList<AhkScript>> ScanAsync(
        IEnumerable<string> folders,
        CancellationToken cancellationToken = default);
}

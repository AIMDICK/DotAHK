namespace DotAHK.Services;

/// <summary>
/// Opens Windows File Explorer at a script's location so the user can reveal the
/// file on disk.
/// </summary>
public interface IFileLocationService
{
    /// <summary>
    /// Opens File Explorer with <paramref name="filePath"/> selected. Returns
    /// false when the file (and its folder) is missing or Explorer could not be
    /// started.
    /// </summary>
    bool RevealInExplorer(string filePath);
}

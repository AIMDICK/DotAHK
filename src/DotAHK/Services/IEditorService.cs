namespace DotAHK.Services;

/// <summary>
/// Reads and writes the raw text of AutoHotkey script files for the in-app
/// quick-fix editor. Saving preserves the file's existing text encoding (BOM,
/// UTF-16, or UTF-8) so edited scripts keep working with the right interpreter.
/// </summary>
public interface IEditorService
{
    /// <summary>Reads the full text of the script at <paramref name="filePath"/>.</summary>
    Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the script at <paramref name="filePath"/> with
    /// <paramref name="content"/>, keeping the file's original encoding.
    /// </summary>
    Task SaveAsync(string filePath, string content, CancellationToken cancellationToken = default);
}

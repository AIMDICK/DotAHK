using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Produces the throw-away execution copy of a script that DotAHK actually
/// launches. The copy has the <c>#NoTrayIcon</c> directive injected so each
/// script cannot add its own notification-area icon ("tray hijacking"). A single
/// master tray icon owned by the main application is used instead.
/// </summary>
public interface ITempScriptService
{
    /// <summary>
    /// Creates an execution copy of <paramref name="script"/> under the app's
    /// temporary directory with <c>#NoTrayIcon</c> prepended. Returns the original
    /// path unchanged when no injection is requested, when the script is missing,
    /// or when the copy cannot be produced, so execution still succeeds.
    /// </summary>
    Task<string?> CreateExecutionCopyAsync(
        AhkScript script,
        bool injectNoTrayIcon,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a temporary copy created by <see cref="CreateExecutionCopyAsync"/>.
    /// Files outside the temporary directory (for example the user's original
    /// script) are never touched.
    /// </summary>
    void Delete(string? tempPath);
}

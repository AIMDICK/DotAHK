using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Starts and stops AutoHotkey scripts while tracking the exact PID of each
/// executed script. Stopping a script only ever kills that script's PID.
/// </summary>
public interface IProcessTracker
{
    /// <summary>Raised when a script session has started.</summary>
    event EventHandler<ScriptRunSession>? SessionStarted;

    /// <summary>Raised when a script session has stopped (self-exit or killed).</summary>
    event EventHandler<ScriptRunSession>? SessionStopped;

    /// <summary>Snapshot of the currently active sessions.</summary>
    IReadOnlyCollection<ScriptRunSession> ActiveSessions { get; }

    /// <summary>Gets the active session for a script path, if any.</summary>
    bool TryGetSession(string scriptPath, out ScriptRunSession? session);

    /// <summary>
    /// Launches a script. When <paramref name="autoStopAfter"/> is set the script
    /// is killed automatically after the given duration without blocking the UI.
    /// <paramref name="arguments"/> is appended to the interpreter command line so
    /// the script receives them via <c>A_Args</c> / <c>%1%</c>.
    /// </summary>
    Task<ScriptRunSession> StartAsync(
        AhkScript script,
        RunMode mode,
        TimeSpan? autoStopAfter = null,
        string? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the script identified by its path, killing only its PID.</summary>
    Task<bool> StopAsync(string scriptPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops (kills) every tracked script. Used when the application shuts down so
    /// no child processes are left behind.
    /// </summary>
    Task StopAllAsync(CancellationToken cancellationToken = default);
}

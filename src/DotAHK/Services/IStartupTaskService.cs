namespace DotAHK.Services;

/// <summary>
/// Manages the Windows Task Scheduler entry that launches DotAHK automatically at
/// sign-in. The task runs with the highest run level (Administrator) and passes the
/// <c>--minimized</c> switch so the app boots straight into the notification area.
/// </summary>
public interface IStartupTaskService
{
    /// <summary>The task name registered in the Windows Task Scheduler.</summary>
    string TaskName { get; }

    /// <summary>
    /// Returns <c>true</c> when the DotAHK startup task exists in the Task Scheduler.
    /// Implemented by running <c>schtasks /query</c> invisibly and checking the exit code.
    /// </summary>
    bool IsStartupTaskRegistered();

    /// <summary>
    /// Creates (or replaces) the elevated, sign-in-triggered startup task. Requires
    /// administrator rights; returns <c>true</c> on success.
    /// </summary>
    bool RegisterStartupTask();

    /// <summary>
    /// Deletes the startup task. Returns <c>true</c> on success.
    /// </summary>
    bool UnregisterStartupTask();
}

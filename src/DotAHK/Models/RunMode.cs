namespace DotAHK.Models;

/// <summary>
/// How a script was launched and how it should be stopped.
/// </summary>
public enum RunMode
{
    /// <summary>No session is running.</summary>
    Stop = 0,

    /// <summary>Runs until the user toggles it off.</summary>
    Persistent = 1,

    /// <summary>Runs for a fixed, short burst (10 seconds) then stops automatically.</summary>
    Burst = 2,

    /// <summary>Runs for a user-defined duration then stops automatically.</summary>
    Scheduled = 3,
}

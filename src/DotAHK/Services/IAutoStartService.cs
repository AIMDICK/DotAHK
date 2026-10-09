namespace DotAHK.Services;

/// <summary>
/// Registers DotAHK under the current user's <c>Run</c> key so Windows launches
/// it automatically at sign-in (daemon mode).
/// </summary>
public interface IAutoStartService
{
    /// <summary>True when DotAHK is currently registered to run at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Adds or removes the sign-in registration. Returns true on success.</summary>
    bool SetEnabled(bool enabled);
}

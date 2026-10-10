namespace DotAHK.Services;

/// <summary>
/// Reports and requests process elevation. Scripts that must run as administrator
/// are handled by elevating the whole DotAHK process rather than elevating each
/// script individually: one elevated tracker can then watch and stop every child
/// through the ordinary process handle, with no UAC blindness.
/// </summary>
public interface IAdminElevationService
{
    /// <summary>True when the current process is already running elevated.</summary>
    bool IsAdministrator { get; }

    /// <summary>
    /// Relaunches DotAHK elevated through the UAC shell verb and closes the current
    /// instance. Returns <c>true</c> when the elevated instance was launched, or
    /// <c>false</c> when the user declined the prompt or elevation is unavailable
    /// (in which case the current instance keeps running untouched).
    /// </summary>
    bool RestartAsAdmin();
}

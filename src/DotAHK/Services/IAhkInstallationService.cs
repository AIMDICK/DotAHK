using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Detects the AutoHotkey installations available on the machine and resolves
/// which interpreter executable should be used to launch a given script.
/// </summary>
public interface IAhkInstallationService
{
    /// <summary>All installations discovered during the last <see cref="RefreshAsync"/>.</summary>
    IReadOnlyList<AhkInstallation> Installations { get; }

    /// <summary>Re-detects installations from the registry and well-known paths.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the installation matching the requested version, or null.</summary>
    AhkInstallation? GetPreferred(AhkVersion version);

    /// <summary>Returns the default installation (v2 preferred, then v1).</summary>
    AhkInstallation? GetDefault();
}

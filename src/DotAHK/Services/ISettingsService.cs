using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Provides access to the persisted <see cref="AppSettings"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>The currently loaded settings. Mutate then call <see cref="Save"/>.</summary>
    AppSettings Settings { get; }

    /// <summary>Persists the current settings to disk.</summary>
    void Save();

    /// <summary>Re-reads the settings from disk.</summary>
    void Reload();
}

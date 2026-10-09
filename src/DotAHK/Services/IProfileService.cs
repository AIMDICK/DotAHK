namespace DotAHK.Services;

/// <summary>
/// Manages environment profiles (named groups of scripts). Membership is a
/// <b>many-to-many</b> relationship: a script may belong to any number of profiles
/// simultaneously. Each profile owns its own list of script identifiers (file paths),
/// and assignment is additive - adding a script to one profile never removes it from
/// another.
/// </summary>
public interface IProfileService
{
    /// <summary>Raised whenever profiles or their membership change.</summary>
    event EventHandler? Changed;

    /// <summary>Names of the defined profiles, in creation order.</summary>
    IReadOnlyList<string> ProfileNames { get; }

    /// <summary>Name of the currently activated profile, or null when none is active.</summary>
    string? ActiveProfileName { get; }

    /// <summary>Creates a profile. Returns false when the name is empty or taken.</summary>
    bool AddProfile(string name);

    /// <summary>Deletes a profile and its membership. Returns false when unknown.</summary>
    bool RemoveProfile(string name);

    /// <summary>Returns the names of every profile the script currently belongs to.</summary>
    IReadOnlyList<string> GetScriptProfiles(string scriptKey);

    /// <summary>
    /// Adds the script to the profile. Additive: it is never removed from any other
    /// profile, so overlapping memberships are preserved.
    /// </summary>
    void AddScriptToProfile(string scriptKey, string profileName);

    /// <summary>Removes the script from this profile only, leaving its other memberships intact.</summary>
    void RemoveScriptFromProfile(string scriptKey, string profileName);

    /// <summary>
    /// Replaces the membership of a single profile. Other profiles are NOT modified,
    /// so many-to-many memberships are preserved. Used by the profile editor overlay.
    /// </summary>
    void SetProfileScripts(string profileName, IEnumerable<string> scriptKeys);

    /// <summary>Returns the normalized script paths owned by a profile.</summary>
    IReadOnlyList<string> GetProfileScripts(string profileName);

    /// <summary>Persists the active profile selection without running any side effects.</summary>
    void SetActiveProfile(string? profileName);
}

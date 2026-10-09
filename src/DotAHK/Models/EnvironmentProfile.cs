namespace DotAHK.Models;

/// <summary>
/// A named group of scripts ("Gaming", "Dev", ...). Activating a profile launches
/// the scripts it owns and stops every tracked script that does not belong to it.
/// A script belongs to at most one profile at a time.
/// </summary>
public sealed class EnvironmentProfile
{
    public EnvironmentProfile(string name)
    {
        Name = name;
    }

    /// <summary>User-facing profile name, unique (case-insensitive).</summary>
    public string Name { get; set; }

    /// <summary>
    /// Normalized (lower-case, full) paths of the scripts that belong to this
    /// profile, mirroring <see cref="AhkScript.Key"/>.
    /// </summary>
    public List<string> ScriptKeys { get; set; } = new();
}

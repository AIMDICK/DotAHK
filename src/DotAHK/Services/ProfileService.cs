using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IProfileService"/>. Membership is stored on the profiles
/// themselves as a many-to-many relationship (a script may belong to several
/// profiles at once); every mutation is saved through <see cref="ISettingsService"/>
/// and announced via <see cref="Changed"/>.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly ISettingsService _settings;

    public ProfileService(ISettingsService settings)
    {
        _settings = settings;
        Settings_EnsureShape();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<string> ProfileNames =>
        _settings.Settings.Profiles.Select(p => p.Name).ToList();

    public string? ActiveProfileName => _settings.Settings.ActiveProfileName;

    public bool AddProfile(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || Find(trimmed) is not null)
        {
            return false;
        }

        _settings.Settings.Profiles.Add(new EnvironmentProfile(trimmed));
        Save();
        return true;
    }

    public bool RemoveProfile(string name)
    {
        var profile = Find(name);
        if (profile is null)
        {
            return false;
        }

        _settings.Settings.Profiles.Remove(profile);
        if (string.Equals(_settings.Settings.ActiveProfileName, profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Settings.ActiveProfileName = null;
        }

        Save();
        return true;
    }

    public IReadOnlyList<string> GetScriptProfiles(string scriptKey)
    {
        if (string.IsNullOrEmpty(scriptKey))
        {
            return Array.Empty<string>();
        }

        // Every profile that lists this script as a member (many-to-many).
        return _settings.Settings.Profiles
            .Where(p => p.ScriptKeys.Any(k => string.Equals(k, scriptKey, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();
    }

    public void AddScriptToProfile(string scriptKey, string profileName)
    {
        if (string.IsNullOrEmpty(scriptKey))
        {
            return;
        }

        var profile = Find(profileName);
        if (profile is null)
        {
            return;
        }

        // Idempotent: adding a script that is already a member is a no-op.
        if (profile.ScriptKeys.Any(k => string.Equals(k, scriptKey, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Additive only: other profiles keep their membership untouched.
        profile.ScriptKeys.Add(scriptKey);
        Save();
    }

    public void RemoveScriptFromProfile(string scriptKey, string profileName)
    {
        if (string.IsNullOrEmpty(scriptKey))
        {
            return;
        }

        var profile = Find(profileName);
        if (profile is null)
        {
            return;
        }

        // Only this edge is removed; the script keeps every other membership.
        var removed = profile.ScriptKeys.RemoveAll(
            k => string.Equals(k, scriptKey, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            Save();
        }
    }

    public void SetProfileScripts(string profileName, IEnumerable<string> scriptKeys)
    {
        var profile = Find(profileName);
        if (profile is null)
        {
            return;
        }

        // Preserve input order while dropping blanks and duplicates.
        var desired = new List<string>();
        var desiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in scriptKeys ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(key) && desiredSet.Add(key))
            {
                desired.Add(key);
            }
        }

        // Only this profile's membership changes. Other profiles are left untouched so
        // a script can remain a member of several profiles (many-to-many).
        var unchanged = profile.ScriptKeys.Count == desired.Count &&
            profile.ScriptKeys.All(k => desiredSet.Contains(k));
        if (unchanged)
        {
            return;
        }

        profile.ScriptKeys.Clear();
        profile.ScriptKeys.AddRange(desired);
        Save();
    }

    public IReadOnlyList<string> GetProfileScripts(string profileName)
    {
        var profile = Find(profileName);
        return profile is null ? Array.Empty<string>() : profile.ScriptKeys.ToList();
    }

    public void SetActiveProfile(string? profileName)
    {
        var value = string.IsNullOrEmpty(profileName) ? null : profileName;
        if (string.Equals(_settings.Settings.ActiveProfileName, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Settings.ActiveProfileName = value;
        Save();
    }

    private EnvironmentProfile? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _settings.Settings.Profiles.FirstOrDefault(
            p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void Settings_EnsureShape()
    {
        // Defensive: settings loaded from an older file may deserialize null lists.
        _settings.Settings.Profiles ??= new List<EnvironmentProfile>();
        foreach (var profile in _settings.Settings.Profiles)
        {
            profile.ScriptKeys ??= new List<string>();
        }
    }

    private void Save()
    {
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

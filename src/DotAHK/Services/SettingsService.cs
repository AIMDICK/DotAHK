using System.IO;
using System.Text.Json;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// <c>%LOCALAPPDATA%\DotAHK\settings.json</c>.
/// The file is small, so loading is done synchronously during startup.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;

    public SettingsService()
    {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotAHK");
        _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        Settings = Load();
    }

    public AppSettings Settings { get; private set; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            Normalize(Settings);
            var json = JsonSerializer.Serialize(Settings, SerializerOptions);

            // Write to a temporary file and swap it in so a crash or power loss
            // mid-write can never leave a truncated/corrupt settings.json behind.
            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        catch (IOException)
        {
            // Settings persistence is best-effort; never crash the app over it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Reload() => Settings = Load();

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                if (loaded is not null)
                {
                    Normalize(loaded);
                    return loaded;
                }
            }
        }
        catch (IOException)
        {
            // Fall through to defaults.
        }
        catch (JsonException)
        {
            // Corrupt file: fall through to defaults.
        }

        var defaults = new AppSettings();
        defaults.WatchFolders.AddRange(GetDefaultWatchFolders());
        Normalize(defaults);
        return defaults;
    }

    /// <summary>
    /// Guarantees every persisted collection is non-null after deserialization so a
    /// hand-edited or older settings file can never trigger a null-reference crash.
    /// </summary>
    private static void Normalize(AppSettings settings)
    {
        settings.WatchFolders ??= new List<string>();
        settings.IgnoredScripts ??= new List<string>();
        settings.AutoStartScripts ??= new List<string>();
        settings.ScriptArguments ??= new Dictionary<string, string>();
        settings.ScriptHotkeys ??= new Dictionary<string, string>();
        settings.Profiles ??= new List<EnvironmentProfile>();

        foreach (var profile in settings.Profiles)
        {
            profile.ScriptKeys ??= new List<string>();
        }
    }

    /// <summary>
    /// The standard user directories scanned by default on first launch.
    /// The full system drive is intentionally never scanned.
    /// </summary>
    private static IEnumerable<string> GetDefaultWatchFolders()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        return candidates.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
    }
}

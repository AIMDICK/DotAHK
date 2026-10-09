using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Centralizes runtime localization: resource lookup, the supported language list, applying
/// the language, and the first-run OS-language detection.
///
/// The strings live in <c>Strings/<lang>/Resources.resw</c> and are copied next to the
/// executable (see the project file). They are parsed directly at run time rather than
/// through MRT Core or the UWP <c>ResourceLoader</c>: in an unpackaged WinUI 3 app the
/// framework resource index is not reliably consumable from code (the UWP loader fail-fasts
/// and the MRT Core map resolves to nothing), so a deterministic file-based loader is used
/// instead.
///
/// XAML text reaches this class through <see cref="ViewModels.LocalizedStrings"/> bindings.
/// The framework's <c>x:Uid</c> cannot follow a runtime language override in an unpackaged
/// app (it always resolves the OS language), so it is deliberately NOT used for the UI.
/// </summary>
public static class LocalizationService
{
    /// <summary>Strict fallback used when no supported OS language is detected.</summary>
    public const string FallbackLanguage = "en-US";

    private static readonly string[] Supported = { "en-US", "es-ES", "ru-RU", "de-DE" };

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Dictionary<string, string>?> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    private static string _current = FallbackLanguage;

    /// <summary>The languages offered by the language selector.</summary>
    public static IReadOnlyList<string> SupportedLanguages => Supported;

    /// <summary>The currently applied language.</summary>
    public static string CurrentLanguage => _current;

    /// <summary>Returns the localized string for <paramref name="key"/> (or the key if missing).</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        var value = Lookup(_current, key);
        if (string.IsNullOrEmpty(value) &&
            !string.Equals(_current, FallbackLanguage, StringComparison.OrdinalIgnoreCase))
        {
            // Fall back to the neutral language so a missing translation never shows a raw key.
            value = Lookup(FallbackLanguage, key);
        }

        return string.IsNullOrEmpty(value) ? key : value;
    }

    private static string? Lookup(string language, string key)
    {
        var catalog = GetCatalog(language);
        return catalog is not null && catalog.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<string, string>? GetCatalog(string language)
    {
        lock (Gate)
        {
            if (Catalogs.TryGetValue(language, out var cached))
            {
                return cached;
            }

            var catalog = LoadCatalog(language);
            Catalogs[language] = catalog;
            return catalog;
        }
    }

    private static Dictionary<string, string>? LoadCatalog(string language)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw");
            if (!File.Exists(path))
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var data in XDocument.Load(path).Descendants("data"))
            {
                var name = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;
                if (!string.IsNullOrEmpty(name) && value is not null)
                {
                    result[name] = value;
                }
            }

            return result;
        }
        catch
        {
            // A malformed or missing catalog must never break the app; Get surfaces the key.
            return null;
        }
    }

    /// <summary>Returns the localized string for <paramref name="key"/> formatted with args.</summary>
    public static string Format(string key, params object?[] args)
    {
        var format = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>Maps any language tag to one of the supported languages (strict en-US fallback).</summary>
    public static string Normalize(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return FallbackLanguage;
        }

        var lower = languageTag.Trim().ToLowerInvariant();
        if (lower.StartsWith("es", StringComparison.Ordinal))
        {
            return "es-ES";
        }

        if (lower.StartsWith("ru", StringComparison.Ordinal))
        {
            return "ru-RU";
        }

        if (lower.StartsWith("de", StringComparison.Ordinal))
        {
            return "de-DE";
        }

        return FallbackLanguage;
    }

    /// <summary>
    /// Applies the language. The UI strings are read from disk on demand, so switching the
    /// language only needs to update <see cref="CurrentLanguage"/>; the WinRT override is
    /// updated as a best-effort for any framework-provided text (file dialogs, etc.).
    /// </summary>
    public static void Apply(string language)
    {
        var normalized = Normalize(language);
        _current = normalized;

        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = normalized;
        }
        catch
        {
            // Best-effort; a failure here does not affect our own catalog lookups.
        }
    }

    /// <summary>Reads the OS display language (first preference) mapped to a supported language.</summary>
    public static string DetectOsLanguage()
    {
        try
        {
            var languages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            if (languages.Count > 0)
            {
                return Normalize(languages[0]);
            }
        }
        catch
        {
            // Fall through to the strict default.
        }

        return FallbackLanguage;
    }

    /// <summary>
    /// Applies the language synchronously and as early as possible (before the first
    /// window/page is created). Uses the persisted choice, or the OS language on the
    /// very first launch. Safe to call before the service graph is initialized.
    /// </summary>
    public static string ApplyEarly()
    {
        string language;
        try
        {
            language = ReadPersistedLanguage() ?? DetectOsLanguage();
        }
        catch
        {
            language = DetectOsLanguage();
        }

        Apply(language);

        // Warm the catalog now so the first bound string is served without touching disk on
        // the UI thread during page construction.
        GetCatalog(_current);
        return _current;
    }

    /// <summary>
    /// Ensures the chosen language is persisted (first-run detection) and re-applies it.
    /// Kept for the loading page; the actual value already came from <see cref="ApplyEarly"/>.
    /// </summary>
    public static string Initialize(ISettingsService settings)
    {
        try
        {
            var saved = settings.Settings.Language;
            if (string.IsNullOrWhiteSpace(saved))
            {
                saved = Normalize(_current);
                settings.Settings.Language = saved;
                settings.Save();
            }

            var normalized = Normalize(saved);
            Apply(normalized);
            return normalized;
        }
        catch
        {
            return _current;
        }
    }

    /// <summary>Reads the persisted language straight from settings.json without services.</summary>
    private static string? ReadPersistedLanguage()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DotAHK",
                "settings.json");

            if (!File.Exists(path))
            {
                return null;
            }

            var journal = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(journal)?.Language;
        }
        catch
        {
            return null;
        }
    }
}

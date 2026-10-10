using System.IO;
using System.Text.RegularExpressions;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IScriptScanner"/> implementation. Enumeration runs on a
/// background thread; symbol and reparse-point folders are skipped to avoid
/// crawling outside the watch list.
/// </summary>
public sealed partial class ScriptScanner : IScriptScanner
{
    private static readonly string[] SupportedExtensions = { ".ahk", ".ahk1", ".ahk2" };

    private readonly IAhkInstallationService _installationService;

    public ScriptScanner(IAhkInstallationService installationService)
    {
        _installationService = installationService;
    }

    public async Task<IReadOnlyList<AhkScript>> ScanAsync(
        IEnumerable<string> folders,
        CancellationToken cancellationToken = default)
    {
        var roots = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scripts = await Task.Run(
            () => ScanFolders(roots, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return scripts;
    }

    private List<AhkScript> ScanFolders(IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
            MatchType = MatchType.Simple,
        };

        var results = new List<AhkScript>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultInstallation = _installationService.GetDefault();

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsSupportedExtension(file) || !seen.Add(file))
                {
                    continue;
                }

                results.Add(CreateScript(file, defaultInstallation));
            }
        }

        return results
            .OrderBy(s => s.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private AhkScript CreateScript(string filePath, AhkInstallation? defaultInstallation)
    {
        long size = 0;
        var lastWrite = DateTime.MinValue;
        try
        {
            var info = new FileInfo(filePath);
            size = info.Length;
            lastWrite = info.LastWriteTimeUtc;
        }
        catch (IOException)
        {
        }

        var script = new AhkScript(filePath)
        {
            FileSizeBytes = size,
            LastWriteTimeUtc = lastWrite,
        };

        script.DetectedVersion = DetectScriptVersion(filePath);
        script.RequiresAdmin = DetectRequiresAdmin(filePath);
        script.DetectedHotkeys.AddRange(HotkeyParser.ExtractHotkeys(filePath));

        var targetVersion = script.DetectedVersion != AhkVersion.Unknown
            ? script.DetectedVersion
            : defaultInstallation?.Version ?? AhkVersion.Unknown;

        script.Installation = _installationService.GetPreferred(targetVersion) ?? defaultInstallation;
        return script;
    }

    private static bool IsSupportedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the first lines of a script looking for a version directive such as
    /// <c>#Requires AutoHotkey v2.0</c>. Returns <see cref="AhkVersion.Unknown"/>
    /// when no directive is present (the caller then uses the default install).
    /// </summary>
    private static AhkVersion DetectScriptVersion(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            for (var i = 0; i < 40 && reader.ReadLine() is { } line; i++)
            {
                var match = RequiresDirectiveRegex().Match(line);
                if (match.Success)
                {
                    return match.Groups["major"].Value == "2" ? AhkVersion.V2 : AhkVersion.V1;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return AhkVersion.Unknown;
    }

    /// <summary>
    /// Returns true when the script source contains a common AutoHotkey elevation
    /// pattern: <c>A_IsAdmin</c> (used by the "restart as admin" idiom) or the
    /// <c>*RunAs</c> launch verb. This is a cheap, whole-file heuristic.
    /// </summary>
    private static bool DetectRequiresAdmin(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            return text.Contains("A_IsAdmin", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("*RunAs", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex(
        @"#Requires\s+AutoHotkey\s+v?(?<major>[12])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiresDirectiveRegex();
}

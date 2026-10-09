using System.Diagnostics;
using System.IO;
using DotAHK.Models;
using Microsoft.Win32;

namespace DotAHK.Services;

/// <summary>
/// Detects AutoHotkey installations by reading the registry
/// (<c>HKLM\SOFTWARE\AutoHotkey</c>, the 32-bit view, and <c>HKCU</c>) and by
/// probing the well-known install directories.
/// </summary>
public sealed class AhkInstallationService : IAhkInstallationService
{
    /// <summary>
    /// Interpreter file names in order of preference. The architecture-specific
    /// binaries come first because launching them directly yields a stable PID
    /// (the generic "AutoHotkey.exe" is a launcher that may re-exec a child).
    /// </summary>
    private static readonly string[] InterpreterPreference =
    {
        "AutoHotkey64.exe",   // v2 x64
        "AutoHotkeyU64.exe",  // v1.1 Unicode x64
        "AutoHotkey32.exe",   // v2 x86
        "AutoHotkeyU32.exe",  // v1.1 Unicode x86
        "AutoHotkey64_UIA.exe",
        "AutoHotkey32_UIA.exe",
        "AutoHotkey.exe",     // fallback launcher
    };

    private readonly List<AhkInstallation> _installations = new();

    public IReadOnlyList<AhkInstallation> Installations => _installations;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var found = await Task.Run(DetectInstallations, cancellationToken).ConfigureAwait(false);

        _installations.Clear();
        _installations.AddRange(found);
    }

    public AhkInstallation? GetPreferred(AhkVersion version)
    {
        if (version == AhkVersion.Unknown)
        {
            return GetDefault();
        }

        return _installations.FirstOrDefault(i => i.Version == version);
    }

    public AhkInstallation? GetDefault()
    {
        return _installations.FirstOrDefault(i => i.Version == AhkVersion.V2)
            ?? _installations.FirstOrDefault(i => i.Version == AhkVersion.V1)
            ?? _installations.FirstOrDefault();
    }

    private static List<AhkInstallation> DetectInstallations()
    {
        var results = new List<AhkInstallation>();
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? installDir, AhkVersion hint, string? displayVersion)
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            {
                return;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(installDir).TrimEnd('\\');
            }
            catch (Exception)
            {
                return;
            }

            if (!seenDirectories.Add(normalized))
            {
                return;
            }

            var installation = BuildInstallation(normalized, hint, displayVersion);
            if (installation is not null)
            {
                results.Add(installation);
            }
        }

        // 1) Registry: 64-bit view, 32-bit view, then per-user.
        var registrySources = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        };

        foreach (var (hive, view) in registrySources)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\AutoHotkey");
                if (key is null)
                {
                    continue;
                }

                var installDir = key.GetValue("InstallDir") as string;
                var versionText = key.GetValue("Version") as string;
                var hint = ParseVersion(versionText);

                // The registry InstallDir may be the root that hosts versioned
                // sub-folders (for example "...\AutoHotkey\v2").
                TryAdd(installDir, hint, versionText);
                if (!string.IsNullOrWhiteSpace(installDir))
                {
                    foreach (var sub in SafeGetDirectories(installDir!))
                    {
                        TryAdd(sub, AhkVersion.Unknown, null);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                // Ignore inaccessible registry hives.
            }
        }

        // 2) Well-known fallback locations.
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var rootAhk = Path.Combine(root, "AutoHotkey");
            TryAdd(rootAhk, AhkVersion.Unknown, null);

            foreach (var sub in SafeGetDirectories(rootAhk))
            {
                TryAdd(sub, AhkVersion.Unknown, null);
            }
        }

        return results
            .OrderByDescending(i => i.Version)
            .ThenBy(i => i.InstallDirectory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AhkInstallation? BuildInstallation(string directory, AhkVersion hint, string? displayVersion)
    {
        var interpreter = ResolveInterpreter(directory);
        if (interpreter is null)
        {
            return null;
        }

        var version = hint != AhkVersion.Unknown ? hint : DetectVersionFromFile(interpreter);
        displayVersion ??= GetFileVersion(interpreter);

        return new AhkInstallation
        {
            Version = version,
            InterpreterPath = interpreter,
            InstallDirectory = directory,
            DisplayVersion = displayVersion,
        };
    }

    private static string? ResolveInterpreter(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var candidate in InterpreterPreference)
        {
            var path = Path.Combine(directory, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Last resort: any "AutoHotkey*.exe" that is not the UX installer helper.
        return SafeGetFiles(directory, "AutoHotkey*.exe")
            .FirstOrDefault(p => !Path.GetFileName(p)
                .StartsWith("AutoHotkeyUX", StringComparison.OrdinalIgnoreCase));
    }

    private static AhkVersion DetectVersionFromFile(string interpreterPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(interpreterPath);
            return info.FileMajorPart switch
            {
                >= 2 => AhkVersion.V2,
                1 => AhkVersion.V1,
                _ => ParseVersion(info.FileVersion),
            };
        }
        catch (Exception)
        {
            return AhkVersion.Unknown;
        }
    }

    private static string? GetFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static AhkVersion ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AhkVersion.Unknown;
        }

        var normalized = text.Trim().TrimStart('v', 'V');
        if (normalized.StartsWith("2", StringComparison.Ordinal))
        {
            return AhkVersion.V2;
        }

        if (normalized.StartsWith("1", StringComparison.Ordinal))
        {
            return AhkVersion.V1;
        }

        return AhkVersion.Unknown;
    }

    private static IEnumerable<string> SafeGetDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeGetFiles(string path, string pattern)
    {
        try
        {
            return Directory.GetFiles(path, pattern);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}

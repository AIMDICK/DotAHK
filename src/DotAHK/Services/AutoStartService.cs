using System.Diagnostics;
using Microsoft.Win32;

namespace DotAHK.Services;

/// <summary>
/// Registry-backed <see cref="IAutoStartService"/>. Writes a single value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> pointing at the
/// current executable with the <c>--minimized</c> switch so the daemon starts
/// hidden in the notification area.
/// </summary>
public sealed class AutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DotAHK";
    private const string MinimizedSwitch = "--minimized";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key is null)
                {
                    return false;
                }

                key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string BuildCommand()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            try
            {
                exePath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch (Exception)
            {
                exePath = null;
            }
        }

        return exePath is null
            ? $"{ValueName} {MinimizedSwitch}"
            : $"\"{exePath}\" {MinimizedSwitch}";
    }
}

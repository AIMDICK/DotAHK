using System.Diagnostics;
using System.Security.Principal;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IAdminElevationService"/>. Elevates the application by
/// relaunching <see cref="Environment.ProcessPath"/> with the UAC "runas" verb,
/// then closing the unelevated instance so only the elevated one remains.
/// </summary>
public sealed class AdminElevationService : IAdminElevationService
{
    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                // If the role cannot be determined, assume unelevated so the UI still
                // offers the elevation button.
                Debug.WriteLine(
                    $"[AdminElevationService] IsAdministrator check failed: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }

    public bool RestartAsAdmin()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
            };

            // Preserve the original switches (for example "--minimized" from the
            // daemon auto-start) so the elevated instance behaves identically.
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            // A declined UAC prompt raises Win32Exception (ERROR_CANCELLED, 1223);
            // elevation can also be blocked by policy. Keep the current instance.
            Debug.WriteLine(
                $"[AdminElevationService] Elevation cancelled or failed: " +
                $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }

        // The elevated instance is starting; close this unelevated one so it never
        // ends up owning the settings file or the process tracker at the same time.
        Microsoft.UI.Xaml.Application.Current?.Exit();
        return true;
    }
}

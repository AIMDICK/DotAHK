using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace DotAHK.Services;

/// <summary>
/// Task Scheduler-backed startup registration. Uses <c>schtasks.exe</c> so the task
/// can run with the highest run level (Administrator) - something the per-user
/// <c>Run</c> registry key used by <see cref="AutoStartService"/> cannot do. Every
/// invocation is launched invisibly (no console window flashes on screen).
/// </summary>
public sealed class StartupTaskService : IStartupTaskService
{
    /// <summary>Name of the task created in the Windows Task Scheduler.</summary>
    public const string DefaultTaskName = "DotAHK_Startup";

    private const string MinimizedSwitch = "--minimized";

    public string TaskName => DefaultTaskName;

    public bool IsStartupTaskRegistered() =>
        RunSchtasks($"/query /tn \"{DefaultTaskName}\"") == 0;

    public bool RegisterStartupTask()
    {
        var exePath = ResolveExecutablePath();
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }

        // Defining the task from the raw command line inherits Task Scheduler defaults
        // that ruin a long-running daemon: the task is terminated after ~3 days and is
        // forbidden from starting on battery power. Registering from XML lets us switch
        // both off (see BuildTaskXml).
        var xmlPath = Path.GetTempFileName();
        try
        {
            // Task Scheduler expects UTF-16 with a matching XML declaration; the empty
            // file created by GetTempFileName is overwritten in place.
            File.WriteAllText(xmlPath, BuildTaskXml(exePath), Encoding.Unicode);

            var arguments = $"/create /tn \"{DefaultTaskName}\" /xml \"{xmlPath}\" /f";
            return RunSchtasks(arguments) == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[StartupTaskService] register failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            // Never leave the generated definition behind on the user's disk.
            try
            {
                File.Delete(xmlPath);
            }
            catch (Exception)
            {
                // Best-effort cleanup only.
            }
        }
    }

    /// <summary>
    /// Builds the Task Scheduler XML definition used to register the startup task.
    /// It runs at logon as the current interactive user at the highest available run
    /// level (Administrator), passes the <c>--minimized</c> switch, and disables the
    /// defaults that would otherwise kill a background daemon: no execution time limit
    /// (<c>PT0S</c>) and allowed to start and keep running on battery power.
    /// </summary>
    private static string BuildTaskXml(string exePath)
    {
        // The executable path and account name are interpolated into XML text nodes, so
        // they must be XML-escaped.
        var escapedExe = SecurityElement.Escape(exePath) ?? exePath;
        var userId = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? string.Empty;

        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Launch DotAHK at sign-in, elevated and minimized to the notification area.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{userId}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{escapedExe}</Command>
      <Arguments>{MinimizedSwitch}</Arguments>
    </Exec>
  </Actions>
</Task>
""";
    }

    public bool UnregisterStartupTask() =>
        RunSchtasks($"/delete /tn \"{DefaultTaskName}\" /f") == 0;

    /// <summary>
    /// Runs <c>schtasks.exe</c> with the supplied arguments in a hidden window and
    /// returns its exit code (<c>0</c> = success). Returns <c>-1</c> if the process
    /// could not be started.
    /// </summary>
    private static int RunSchtasks(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            // Drain both streams so the child can never block on a full pipe buffer.
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[StartupTaskService] schtasks failed: {ex.GetType().Name}: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Resolves the path of the running executable, falling back to the main module
    /// when <see cref="Environment.ProcessPath"/> is unavailable.
    /// </summary>
    private static string? ResolveExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            return exePath;
        }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

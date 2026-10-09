using System.Diagnostics;
using System.IO;

namespace DotAHK.Services;

/// <summary>
/// Diagnostic startup stopwatch. Records the elapsed milliseconds since process
/// launch for each marked phase to the debug output and to
/// <c>%LOCALAPPDATA%\DotAHK\startup.log</c>. This is used to locate exactly which
/// phase blocks the first paint. Tracing is best-effort and never throws.
/// </summary>
internal static class StartupTrace
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static readonly object Sync = new();
    private static readonly DateTime ProcessStart = GetProcessStart();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DotAHK",
        "startup.log");

    private static bool _sessionStarted;

    /// <summary>Starts a fresh trace session, truncating any previous log file.</summary>
    public static void Start(string label)
    {
        if (_sessionStarted)
        {
            return;
        }

        _sessionStarted = true;

        try
        {
            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
            }
        }
        catch
        {
            // Best effort.
        }

        Mark("=== session start (" + label + ") ===");
    }

    /// <summary>Records a labelled phase with its offset from process launch.</summary>
    public static void Mark(string phase)
    {
        // "sinceProcess" measures from the OS process creation so it also reveals the
        // .NET runtime + Windows App SDK bootstrap time that happens BEFORE our code.
        var sinceProcess = (long)(DateTime.Now - ProcessStart).TotalMilliseconds;
        var line = $"{DateTime.Now:HH:mm:ss.fff}  (proc +{sinceProcess,6} ms)  code +{Watch.ElapsedMilliseconds,6} ms  [T{Environment.CurrentManagedThreadId,2}]  {phase}";
        Debug.WriteLine("[DotAHK.Startup] " + line);

        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Tracing must never affect startup.
        }
    }

    private static DateTime GetProcessStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IProcessTracker"/>. Launches a script through its resolved
/// AutoHotkey interpreter, records the exact process ID, and kills only that PID
/// when the script is stopped or its timer elapses.
/// </summary>
public sealed class ProcessTracker : IProcessTracker, IDisposable
{
    /// <summary>Fixed burst-mode duration (10 seconds per specification).</summary>
    public static readonly TimeSpan BurstDuration = TimeSpan.FromSeconds(10);

    /// <summary>CPU percentage (of one core) above which a session is considered busy.</summary>
    private const double CpuAlertThreshold = 5.0;

    /// <summary>Consecutive busy samples required before flagging a loop.</summary>
    private const int CpuAlertConsecutiveSamples = 3;

    private static readonly TimeSpan TelemetryInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, ScriptRunSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, CpuSample> _cpuSamples =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, int> _cpuStreak =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ITempScriptService _tempScripts;
    private readonly ISettingsService _settings;
    private readonly Timer _telemetryTimer;

    public ProcessTracker(ITempScriptService tempScripts, ISettingsService settings)
    {
        _tempScripts = tempScripts;
        _settings = settings;
        _telemetryTimer = new Timer(_ => SampleTelemetry(), null, TelemetryInterval, TelemetryInterval);
    }

    public event EventHandler<ScriptRunSession>? SessionStarted;
    public event EventHandler<ScriptRunSession>? SessionStopped;

    public IReadOnlyCollection<ScriptRunSession> ActiveSessions => _sessions.Values.ToList();

    public bool TryGetSession(string scriptPath, out ScriptRunSession? session)
    {
        if (_sessions.TryGetValue(Normalize(scriptPath), out var found))
        {
            session = found;
            return true;
        }

        session = null;
        return false;
    }

    public async Task<ScriptRunSession> StartAsync(
        AhkScript script,
        RunMode mode,
        TimeSpan? autoStopAfter = null,
        string? arguments = null,
        CancellationToken cancellationToken = default)
    {
        if (script.Installation is null)
        {
            throw new InvalidOperationException(
                $"No AutoHotkey installation could be resolved for '{script.FilePath}'. " +
                "Install AutoHotkey v1 or v2, then rescan.");
        }

        // Burst mode always uses the fixed 10 second window.
        if (mode == RunMode.Burst && autoStopAfter is null)
        {
            autoStopAfter = BurstDuration;
        }

        var key = script.Key;

        if (_sessions.TryGetValue(key, out var existing) && !existing.HasExited)
        {
            // Only reuse the running process when neither the mode nor the
            // arguments changed; otherwise the script must be relaunched.
            if (existing.Mode == mode &&
                string.Equals(existing.Arguments, arguments, StringComparison.Ordinal))
            {
                return existing;
            }

            StopSession(existing);
        }

        var session = await LaunchAsync(script, mode, autoStopAfter, arguments, cancellationToken)
            .ConfigureAwait(false);
        _sessions[key] = session;

        SessionStarted?.Invoke(this, session);
        StartAutoStopIfNeeded(session, autoStopAfter);

        return session;
    }

    public Task<bool> StopAsync(string scriptPath, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(Normalize(scriptPath), out var session))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(StopSession(session));
    }

    public Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot first: StopSession mutates the dictionary via exit events.
        foreach (var session in _sessions.Values.ToList())
        {
            StopSession(session);
        }

        return Task.CompletedTask;
    }

    private async Task<ScriptRunSession> LaunchAsync(
        AhkScript script,
        RunMode mode,
        TimeSpan? autoStopAfter,
        string? arguments,
        CancellationToken cancellationToken)
    {
        var executionPath = await ResolveExecutionPathAsync(script, cancellationToken).ConfigureAwait(false)
            ?? script.FilePath;

        // Keep only genuine temporary copies for cleanup: the original script must
        // never be deleted when the session is disposed.
        var tempScriptPath = string.Equals(
            Path.GetFullPath(executionPath),
            Path.GetFullPath(script.FilePath),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : executionPath;

        var startInfo = new ProcessStartInfo
        {
            FileName = script.Installation!.InterpreterPath,
            WorkingDirectory = string.IsNullOrEmpty(script.DirectoryPath)
                ? Environment.CurrentDirectory
                : script.DirectoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Quote the script path so folders with spaces work, then append the
            // user-defined arguments verbatim (for example: "arg1" "arg2").
            Arguments = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{executionPath}\""
                : $"\"{executionPath}\" {arguments}",
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            process.Dispose();
            _tempScripts.Delete(tempScriptPath);
            throw new InvalidOperationException($"Failed to start '{script.FilePath}'.");
        }

        var session = new ScriptRunSession(
            process, script, mode, autoStopAfter, arguments, tempScriptPath);

        // Subscribe to the terminal event first, then to the process exit, so a
        // very fast exit cannot slip through without triggering cleanup.
        session.Exited += OnSessionExited;
        process.Exited += (_, _) => session.SignalExited();

        if (process.HasExited)
        {
            session.SignalExited();
        }

        return session;
    }

    /// <summary>
    /// Returns the path the interpreter should run: a temporary copy with
    /// <c>#NoTrayIcon</c> injected when tray hijacking is enabled, otherwise the
    /// original script.
    /// </summary>
    private Task<string?> ResolveExecutionPathAsync(AhkScript script, CancellationToken cancellationToken) =>
        _tempScripts.CreateExecutionCopyAsync(script, _settings.Settings.TrayHijackingEnabled, cancellationToken);

    private void StartAutoStopIfNeeded(ScriptRunSession session, TimeSpan? autoStopAfter)
    {
        if (autoStopAfter is { } duration && !session.HasExited)
        {
            _ = ScheduleAutoStopAsync(session, duration);
        }
    }

    /// <summary>
    /// Waits asynchronously (without blocking the UI thread) then kills only the
    /// session's specific PID when the timer elapses.
    /// </summary>
    private async Task ScheduleAutoStopAsync(ScriptRunSession session, TimeSpan duration)
    {
        try
        {
            await Task.Delay(duration, session.AutoStopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // The script was stopped or killed before the timer elapsed.
        }

        StopSession(session);
    }

    private bool StopSession(ScriptRunSession session)
    {
        session.CancelAutoStop();
        var killed = session.Kill();

        // Guarantee the exit notification even when the process handle does not
        // raise the Exited event (already exited, access denied, etc.).
        try
        {
            session.Process.WaitForExit(2000);
        }
        catch (Exception)
        {
        }

        if (session.HasExited)
        {
            session.SignalExited();
        }

        return killed;
    }

    private void OnSessionExited(object? sender, ScriptRunSession session)
    {
        // Only remove the entry when it still points at this exact instance: a
        // newer session for the same script may already have replaced it.
        if (_sessions.TryGetValue(session.Script.Key, out var current) &&
            ReferenceEquals(current, session))
        {
            _sessions.TryRemove(session.Script.Key, out _);
        }

        session.CancelAutoStop();

        // Remove the temporary #NoTrayIcon execution copy now that the run ended.
        _tempScripts.Delete(session.TempScriptPath);

        SessionStopped?.Invoke(this, session);

        // Dispose off the exit-callback thread to avoid re-entrancy stalls.
        _ = Task.Run(() =>
        {
            try
            {
                session.Dispose();
            }
            catch (Exception)
            {
            }
        });
    }

    /// <summary>
    /// Samples CPU and memory for every active session and flags sustained high
    /// CPU usage as a probable runaway loop. Runs on a timer thread.
    /// </summary>
    private void SampleTelemetry()
    {
        var now = DateTime.UtcNow;
        foreach (var session in _sessions.Values)
        {
            var key = session.Script.Key;
            try
            {
                if (session.HasExited)
                {
                    _cpuSamples.TryRemove(key, out _);
                    _cpuStreak.TryRemove(key, out _);
                    continue;
                }

                var process = session.Process;
                var cpu = process.TotalProcessorTime;
                var workingSet = process.WorkingSet64;

                double percent = 0;
                if (_cpuSamples.TryGetValue(key, out var previous))
                {
                    var elapsedMs = (now - previous.Timestamp).TotalMilliseconds;
                    var cpuMs = (cpu - previous.Cpu).TotalMilliseconds;
                    if (elapsedMs > 1 && cpuMs >= 0)
                    {
                        percent = cpuMs / (elapsedMs * Environment.ProcessorCount) * 100.0;
                    }
                }

                _cpuSamples[key] = new CpuSample(cpu, now);

                var streak = _cpuStreak.TryGetValue(key, out var current) ? current : 0;
                streak = percent >= CpuAlertThreshold ? streak + 1 : 0;
                _cpuStreak[key] = streak;

                session.UpdateTelemetry(percent, workingSet, streak >= CpuAlertConsecutiveSamples);
            }
            catch (Exception)
            {
                // The process may have vanished between the check and the read.
            }
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).ToLowerInvariant();
        }
        catch (Exception)
        {
            return path.ToLowerInvariant();
        }
    }

    private readonly record struct CpuSample(TimeSpan Cpu, DateTime Timestamp);

    public void Dispose()
    {
        _telemetryTimer.Dispose();

        foreach (var session in _sessions.Values)
        {
            try
            {
                session.CancelAutoStop();
                _tempScripts.Delete(session.TempScriptPath);
                session.Dispose();
            }
            catch (Exception)
            {
            }
        }

        _sessions.Clear();
    }
}

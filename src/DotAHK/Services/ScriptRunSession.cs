using System.ComponentModel;
using System.Diagnostics;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Represents a single running AutoHotkey script instance tracked by
/// <see cref="IProcessTracker"/>. Each session owns exactly one OS process, so
/// stopping a session kills only that specific PID.
/// </summary>
public sealed class ScriptRunSession : IDisposable
{
    private readonly object _sync = new();
    private readonly object _telemetrySync = new();
    private readonly CancellationTokenSource _autoStopCancellation = new();
    private bool _terminated;

    private double _cpuUsagePercent;
    private long _workingSetBytes;
    private bool _isHighCpu;

    internal ScriptRunSession(
        Process process,
        AhkScript script,
        RunMode mode,
        TimeSpan? autoStopAfter,
        string? arguments,
        string? tempScriptPath)
    {
        Process = process;
        Script = script;
        Mode = mode;
        ProcessId = process.Id;
        StartedAt = DateTimeOffset.Now;
        AutoStopAt = autoStopAfter.HasValue ? StartedAt + autoStopAfter.Value : null;
        Arguments = arguments;
        TempScriptPath = tempScriptPath;
    }

    /// <summary>The underlying OS process. Its PID is the one and only PID this session kills.</summary>
    public Process Process { get; }

    /// <summary>The script that is running.</summary>
    public AhkScript Script { get; }

    /// <summary>How the script was launched (persistent, burst, or scheduled).</summary>
    public RunMode Mode { get; }

    /// <summary>The exact process ID captured at launch time.</summary>
    public int ProcessId { get; }

    /// <summary>When the script was started.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When the script should be stopped automatically, if scheduled.</summary>
    public DateTimeOffset? AutoStopAt { get; }

    /// <summary>The extra command-line arguments passed to the script, if any.</summary>
    public string? Arguments { get; }

    /// <summary>
    /// Path of the temporary execution copy used for this run (with the
    /// <c>#NoTrayIcon</c> directive injected), or null when the original script was
    /// launched in place. Cleaned up when the session is disposed.
    /// </summary>
    public string? TempScriptPath { get; }

    internal CancellationToken AutoStopToken => _autoStopCancellation.Token;

    /// <summary>Raised exactly once, when the underlying process has exited.</summary>
    public event EventHandler<ScriptRunSession>? Exited;

    /// <summary>True once the process has exited or was terminated.</summary>
    public bool HasExited
    {
        get
        {
            lock (_sync)
            {
                if (_terminated)
                {
                    return true;
                }

                try
                {
                    return Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }
    }

    /// <summary>Remaining time until the automatic stop, or null for persistent runs.</summary>
    public TimeSpan? Remaining
    {
        get
        {
            if (AutoStopAt is null)
            {
                return null;
            }

            var remaining = AutoStopAt.Value - DateTimeOffset.Now;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>Most recent CPU usage of this session, as a percentage of one core.</summary>
    public double CpuUsagePercent
    {
        get
        {
            lock (_telemetrySync)
            {
                return _cpuUsagePercent;
            }
        }
    }

    /// <summary>Most recent working-set (physical memory) size, in bytes.</summary>
    public long WorkingSetBytes
    {
        get
        {
            lock (_telemetrySync)
            {
                return _workingSetBytes;
            }
        }
    }

    /// <summary>
    /// True when the tracker has observed sustained high CPU for this session,
    /// which usually indicates a runaway (loop) script.
    /// </summary>
    public bool IsHighCpu
    {
        get
        {
            lock (_telemetrySync)
            {
                return _isHighCpu;
            }
        }
    }

    /// <summary>Called by the tracker with a fresh sample for this session.</summary>
    internal void UpdateTelemetry(double cpuUsagePercent, long workingSetBytes, bool isHighCpu)
    {
        lock (_telemetrySync)
        {
            _cpuUsagePercent = cpuUsagePercent;
            _workingSetBytes = workingSetBytes;
            _isHighCpu = isHighCpu;
        }
    }

    internal void SignalExited()
    {
        lock (_sync)
        {
            if (_terminated)
            {
                return;
            }

            _terminated = true;
        }

        Exited?.Invoke(this, this);
    }

    /// <summary>Cancels any pending auto-stop timer.</summary>
    internal void CancelAutoStop()
    {
        try
        {
            _autoStopCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Kills only this session's specific PID. Cleanup (dictionary removal,
    /// event raising) is driven by the Exited event.
    /// </summary>
    internal bool Kill()
    {
        lock (_sync)
        {
            if (_terminated)
            {
                return false;
            }

            try
            {
                if (Process.HasExited)
                {
                    return false;
                }

                Process.Kill();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        CancelAutoStop();
        _autoStopCancellation.Dispose();
        Process.Dispose();
    }
}

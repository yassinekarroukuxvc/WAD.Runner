using System;
using System.Diagnostics;
using System.Threading;

using WAD.Runner.Application;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// Watches the current SolidWorks automation attempt. The watchdog is never
/// allowed to kill an unrelated SolidWorks process.
/// </summary>
public sealed class SolidWorksStallWatchdog : IDisposable
{
    private readonly TimeSpan _timeout;
    private readonly string _label;
    private readonly Timer? _timer;
    private int _processId;
    private int _fired;

    private SolidWorksStallWatchdog(TimeSpan timeout, string label)
    {
        _timeout = timeout;
        _label = label;
        if (timeout > TimeSpan.Zero)
        {
            Logger.MarkActivity();
            _timer = new Timer(Check, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
    }

    public static SolidWorksStallWatchdog Start(int timeoutSeconds, string label)
        => new(TimeSpan.FromSeconds(Math.Max(0, timeoutSeconds)), label);

    public bool Fired => Volatile.Read(ref _fired) == 1;

    /// <summary>Register only a PID obtained from a WAD.Runner-owned SwSession.</summary>
    public void WatchProcess(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        Volatile.Write(ref _processId, processId);
        Logger.MarkActivity();
    }

    private void Check(object? _)
    {
        if (Fired)
            return;

        var idle = DateTime.UtcNow - Logger.LastActivityUtc;
        if (idle < _timeout || Interlocked.Exchange(ref _fired, 1) == 1)
            return;

        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        var pid = Volatile.Read(ref _processId);
        Logger.Error($"[SW-Watchdog] {_label}: no progress for {idle.TotalSeconds:0} s. Owned PID={pid}.");

        if (pid <= 0)
        {
            Logger.Error("[SW-Watchdog] No owned PID registered; refusing to terminate arbitrary SolidWorks processes.");
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || !process.ProcessName.Equals("SLDWORKS", StringComparison.OrdinalIgnoreCase))
                return;

            Logger.Warn($"[SW-Watchdog] Terminating stalled WAD-owned SolidWorks PID={pid}.");
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* Already exited. */ }
        catch (InvalidOperationException) { /* Already exited. */ }
        catch (Exception ex) { Logger.Warn($"[SW-Watchdog] Failed to terminate owned PID={pid}: {ex.Message}"); }
    }

    public void Dispose() => _timer?.Dispose();
}

public sealed class SolidWorksStallException : Exception
{
    public SolidWorksStallException(string message, Exception? inner = null) : base(message, inner) { }
}

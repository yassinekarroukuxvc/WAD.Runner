using System;
using System.Threading;

using WAD.Runner.Application;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// Stops a hung SolidWorks. While a job runs, every log line counts as progress; when nothing is
/// logged for the timeout, SolidWorks is killed. The blocked COM call then fails on the STA thread,
/// which ends the attempt so it can be retried with a fresh session.
/// </summary>
public sealed class SolidWorksStallWatchdog : IDisposable
{
    private readonly TimeSpan _timeout;
    private readonly string _label;
    private readonly Timer? _timer;
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

    private void Check(object? _)
    {
        var idle = DateTime.UtcNow - Logger.LastActivityUtc;
        if (idle < _timeout || Interlocked.Exchange(ref _fired, 1) == 1)
            return;

        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        Logger.Error($"[SW-Watchdog] {_label}: no progress for {idle.TotalSeconds:0} s; stopping the hung SolidWorks.");
        SolidWorksProcessKiller.KillAll(killVbaServer: true);
    }

    public void Dispose() => _timer?.Dispose();
}

public sealed class SolidWorksStallException : Exception
{
    public SolidWorksStallException(string message, Exception? inner = null) : base(message, inner) { }
}

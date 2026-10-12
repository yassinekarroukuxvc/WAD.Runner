using System;

using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// Serial SolidWorks lifecycle manager. Calls must run on the same STA worker.
/// API callers may reuse an owned process across drawing types within ONE job,
/// but MUST call DropSession in their outer Execute finally block.
/// </summary>
public sealed class SolidWorksSessionManager : IDisposable
{
    private readonly ISwSessionFactory _factory;
    private SwSession? _session;
    private int _jobsOnSession;

    public SolidWorksSessionManager(
        ISwSessionFactory factory,
        Microsoft.Extensions.Configuration.IConfiguration? configuration)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Options = SolidWorksPerformanceOptions.From(configuration);
        Logger.Info(
            $"[SW-Session] ShareSessionPerJob={Options.ShareSessionPerJob}, " +
            $"KeepSessionBetweenJobs={Options.KeepSessionBetweenJobs}, " +
            $"RestartSessionAfterJobs={Options.RestartSessionAfterJobs}, " +
            $"StallTimeoutSeconds={Options.StallTimeoutSeconds}, StallRetries={Options.StallRetries}.");
    }

    public SolidWorksPerformanceOptions Options { get; }

    // Kept for compatibility with existing consumers; API execution now
    // unconditionally closes its session at its job boundary.
    public bool KeepsSessionAlive => false;

    public string? RunJob(
        string label,
        bool visible,
        bool keepAliveAllowed,
        Func<SldWorks, string?> modelPhase,
        Action<SldWorks, string?> drawingPhase)
    {
        var attempts = 1 + Math.Max(0, Options.StallRetries);

        for (var attempt = 1; ; attempt++)
        {
            using var watchdog = SolidWorksStallWatchdog.Start(Options.StallTimeoutSeconds, label);

            try
            {
                var result = Options.ShareSessionPerJob
                    ? RunShared(visible, keepAliveAllowed, modelPhase, drawingPhase, watchdog)
                    : RunSeparate(visible, modelPhase, drawingPhase, watchdog);

                if (watchdog.Fired)
                    throw new SolidWorksStallException($"{label}: SolidWorks hung and was stopped.");

                return result;
            }
            catch (Exception ex) when (watchdog.Fired || ex is SolidWorksStallException)
            {
                DropSession();
                if (attempt >= attempts)
                    throw ex as SolidWorksStallException
                        ?? new SolidWorksStallException($"{label}: SolidWorks hung and was stopped.", ex);

                Logger.Warn($"[SW-Session] {label}: retrying after SolidWorks stall ({attempt + 1}/{attempts}).");
            }
            catch
            {
                DropSession();
                throw;
            }
        }
    }

    private string? RunShared(
        bool visible,
        bool keepAliveAllowed,
        Func<SldWorks, string?> modelPhase,
        Action<SldWorks, string?> drawingPhase,
        SolidWorksStallWatchdog watchdog)
    {
        var app = Acquire(visible, watchdog);
        var modelResult = modelPhase(app);

        CloseAllDocuments(app);
        drawingPhase(app, modelResult);

        _jobsOnSession++;
        // 'keepAliveAllowed' means the outer API request has another drawing
        // type to run, not that SolidWorks should survive the API request.
        // The API executor's finally block closes the process after the last type.
        var keepForCurrentRequest = keepAliveAllowed &&
            (Options.RestartSessionAfterJobs <= 0 || _jobsOnSession < Options.RestartSessionAfterJobs);
        if (keepForCurrentRequest)
            CloseAllDocuments(app);
        else
            DropSession();

        return modelResult;
    }

    private string? RunSeparate(
        bool visible,
        Func<SldWorks, string?> modelPhase,
        Action<SldWorks, string?> drawingPhase,
        SolidWorksStallWatchdog watchdog)
    {
        DropSession();

        string? modelResult;
        using (var model = _factory.Create(visible))
        {
            watchdog.WatchProcess(model.ProcessId);
            modelResult = modelPhase(model.App);
        }

        using (var drawing = _factory.Create(visible))
        {
            watchdog.WatchProcess(drawing.ProcessId);
            drawingPhase(drawing.App, modelResult);
        }

        return modelResult;
    }

    private SldWorks Acquire(bool visible, SolidWorksStallWatchdog watchdog)
    {
        if (_session is not null && IsAlive(_session.App))
        {
            CloseAllDocuments(_session.App);
            _session.ApplyVisibility(visible);
            watchdog.WatchProcess(_session.ProcessId);
            Logger.Info($"[SW-Session] Reusing WAD-owned PID={_session.ProcessId} within current API job.");
            return _session.App;
        }

        DropSession();
        // No KillAll: SwSession refuses to attach to any existing user process.
        _session = _factory.Create(visible);
        _jobsOnSession = 0;
        watchdog.WatchProcess(_session.ProcessId);
        return _session.App;
    }

    private static bool IsAlive(SldWorks app)
    {
        try { _ = app.RevisionNumber(); return true; }
        catch { return false; }
    }

    private static void CloseAllDocuments(SldWorks app)
    {
        try { app.CloseAllDocuments(true); }
        catch (Exception ex) { Logger.Warn($"[SW-Session] CloseAllDocuments failed: {ex.Message}"); }
    }

    /// <summary>Close only the process created by this manager.</summary>
    public void DropSession()
    {
        var session = _session;
        _session = null;
        _jobsOnSession = 0;

        if (session is null)
            return;

        try { session.Dispose(); }
        catch (Exception ex) { Logger.Warn($"[SW-Session] Owned-session shutdown failed: {ex}"); }
    }

    public void Dispose() => DropSession();
}

using System;


using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// Runs one job (model phase, then drawing phase) against SolidWorks, following
/// <see cref="SolidWorksPerformanceOptions"/>:
///
///     shared session : one SolidWorks for both phases (documents closed in between)
///     keep alive     : the session stays open for the next job (API worker)
///     watchdog       : a hung SolidWorks is stopped and the job retried with a fresh session
///
/// With every option off it behaves as before: kill SolidWorks, a fresh session per phase.
/// All calls must come from the one STA thread that drives SolidWorks.
/// </summary>
public sealed class SolidWorksSessionManager : IDisposable
{
    private readonly ISwSessionFactory _factory;
    private SwSession? _session;
    private int _jobsOnSession;

    public SolidWorksSessionManager(ISwSessionFactory factory, Microsoft.Extensions.Configuration.IConfiguration? configuration)
    {
        _factory = factory;
        Options = SolidWorksPerformanceOptions.From(configuration);
        Logger.Info(
            $"[SW-Session] ShareSessionPerJob={Options.ShareSessionPerJob}, " +
            $"KeepSessionBetweenJobs={Options.KeepSessionBetweenJobs}, " +
            $"RestartSessionAfterJobs={Options.RestartSessionAfterJobs}, " +
            $"StallTimeoutSeconds={Options.StallTimeoutSeconds}, StallRetries={Options.StallRetries}.");
    }

    public SolidWorksPerformanceOptions Options { get; }

    /// <summary>True when sessions outlive a job, so callers must not kill SolidWorks around it.</summary>
    public bool KeepsSessionAlive => Options.ShareSessionPerJob && Options.KeepSessionBetweenJobs;

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
                    ? RunShared(visible, keepAliveAllowed, modelPhase, drawingPhase)
                    : RunSeparate(visible, modelPhase, drawingPhase);

                // Some steps catch COM errors and carry on; a stopped SolidWorks must not pass as success.
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

                Logger.Warn($"[SW-Session] {label}: retrying after a SolidWorks hang (attempt {attempt + 1}/{attempts}).");
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
        Action<SldWorks, string?> drawingPhase)
    {
        var app = Acquire(visible);

        var modelResult = modelPhase(app);
        CloseAllDocuments(app);
        drawingPhase(app, modelResult);

        _jobsOnSession++;
        var keep = keepAliveAllowed && Options.KeepSessionBetweenJobs &&
                   (Options.RestartSessionAfterJobs <= 0 || _jobsOnSession < Options.RestartSessionAfterJobs);
        if (keep)
            CloseAllDocuments(app);
        else
            DropSession();

        return modelResult;
    }

    // Previous behaviour: a fresh SolidWorks for each phase.
    private string? RunSeparate(bool visible, Func<SldWorks, string?> modelPhase, Action<SldWorks, string?> drawingPhase)
    {
        DropSession();
        SolidWorksProcessKiller.KillAll(killVbaServer: true);

        string? modelResult;
        using (var model = _factory.Create(visible))
            modelResult = modelPhase(model.App);

        using (var drawing = _factory.Create(visible))
            drawingPhase(drawing.App, modelResult);

        return modelResult;
    }

    private SldWorks Acquire(bool visible)
    {
        if (_session is not null && IsAlive(_session.App))
        {
            CloseAllDocuments(_session.App);
            if (_session.App.Visible != visible)
                _session.App.Visible = visible;

            Logger.Info($"[SW-Session] Reusing SolidWorks session (job {_jobsOnSession + 1} on it).");
            return _session.App;
        }

        DropSession();
        SolidWorksProcessKiller.KillAll(killVbaServer: true);
        _session = _factory.Create(visible);
        _jobsOnSession = 0;
        return _session.App;
    }

    private static bool IsAlive(SldWorks app)
    {
        try
        {
            _ = app.RevisionNumber();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CloseAllDocuments(SldWorks app)
    {
        try { app.CloseAllDocuments(true); }
        catch (Exception ex) { Logger.Warn($"[SW-Session] CloseAllDocuments failed: {ex.Message}"); }
    }

    /// <summary>Closes the current session, if any (ExitApp, then makes sure no process is left).</summary>
    public void DropSession()
    {
        if (_session is null)
            return;

        try { _session.Dispose(); }
        catch (Exception ex) { Logger.Warn($"[SW-Session] Closing SolidWorks failed: {ex.Message}"); }
        finally
        {
            _session = null;
            _jobsOnSession = 0;
            SolidWorksProcessKiller.KillAll(killVbaServer: true);
        }
    }

    public void Dispose() => DropSession();
}

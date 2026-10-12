using Microsoft.Extensions.Configuration;

namespace WAD.Runner.Solidworks.Adapters;

/// <summary>
/// "Runner:SolidWorks:Performance" settings. Setting ShareSessionPerJob and KeepSessionBetweenJobs
/// to false and StallTimeoutSeconds to 0 restores the previous behaviour: kill SolidWorks, start
/// one session for the model phase and a second for the drawing phase, kill it after the job.
/// </summary>
public sealed class SolidWorksPerformanceOptions
{
    /// <summary>Model and drawing phase of a job share one SolidWorks session.</summary>
    public bool ShareSessionPerJob { get; init; } = true;

    /// <summary>
    /// Legacy configuration switch; the API executor now ALWAYS closes its
    /// owned session after a complete HTTP job, even if this is set to true.
    /// Between drawing types of the same HTTP job, the session is reused.
    /// </summary>
    public bool KeepSessionBetweenJobs { get; init; } = false;

    /// <summary>Start a fresh session after this many jobs on one session (0 = never).</summary>
    public int RestartSessionAfterJobs { get; init; } = 0;

    /// <summary>No log output for this long during a job means SolidWorks hung (0 = no watchdog).</summary>
    public int StallTimeoutSeconds { get; init; } = 180;

    /// <summary>How often a job is retried after the watchdog stopped a hung SolidWorks.</summary>
    public int StallRetries { get; init; } = 0;

    public static SolidWorksPerformanceOptions From(IConfiguration? configuration)
        => configuration?.GetSection("Runner:SolidWorks:Performance").Get<SolidWorksPerformanceOptions>()
           ?? new SolidWorksPerformanceOptions();
}

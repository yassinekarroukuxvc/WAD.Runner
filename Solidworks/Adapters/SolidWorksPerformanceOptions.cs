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
    /// API worker only: keep that session open between jobs (documents are closed after each job).
    /// Needs ShareSessionPerJob. The CLI is one process per run and always closes SolidWorks.
    /// </summary>
    public bool KeepSessionBetweenJobs { get; init; } = true;

    /// <summary>Start a fresh session after this many jobs on one session (0 = never).</summary>
    public int RestartSessionAfterJobs { get; init; } = 20;

    /// <summary>No log output for this long during a job means SolidWorks hung (0 = no watchdog).</summary>
    public int StallTimeoutSeconds { get; init; } = 180;

    /// <summary>How often a job is retried after the watchdog stopped a hung SolidWorks.</summary>
    public int StallRetries { get; init; } = 1;

    public static SolidWorksPerformanceOptions From(IConfiguration? configuration)
        => configuration?.GetSection("Runner:SolidWorks:Performance").Get<SolidWorksPerformanceOptions>()
           ?? new SolidWorksPerformanceOptions();
}

// WAD.Runner.Api/ApiHost.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Channels;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using WAD.Runner.Application.Ports;
using WAD.Runner.Application.UseCases;

using WAD.Runner.DataManagement.Infrastructure.Serialization;
using WAD.Runner.DataManagement.Infrastructure.Adapters;
using WAD.Runner.DataManagement.Infrastructure.Sqlite;   // ProAlphaRepository, SqliteWedgeDataSource

using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Execution;

using WAD.Runner.SolidWorks.Adapters;
using WAD.Runner.Solidworks.Adapters;                  // ISwSessionFactory, SwServiceFactory

namespace WAD.Runner.Api;

// ---------- Options used by the API host ----------
public sealed class RunnerOptions
{
    public string? Urls { get; set; }

    [Required]
    public string ApiKey { get; set; } = "dev-key-change-me";

    public int QueueCapacity { get; set; } = 64;
}

// ---------- Models / DTOs ----------
public record RunRequest
{
    public List<string>? ArticleNumbers { get; init; }

    // New style (preferred)
    public List<string>? DrawingTypes { get; init; }

    // Single-type fallback
    public string? DrawingType { get; init; } = "Production";

    public string Subclass { get; init; } = "FG";       // FG | PGB

    // NOTE: We still validate it, but /run will force OutputFolder into JobsRoot/<jobId>/results.
    public string? OutputFolder { get; init; }

    // Legacy style (your web app currently uses Options["drawingTypes"])
    public Dictionary<string, string>? Options { get; init; }
}

public static class RunRequestValidator
{
    public static List<string> Validate(RunRequest r)
    {
        var errors = new List<string>();

        if (r.ArticleNumbers is null || r.ArticleNumbers.Count == 0)
            errors.Add("articleNumbers must contain at least one value.");

        var subclass = (r.Subclass ?? string.Empty).Trim().ToUpperInvariant();
        if (subclass is not ("FG" or "PGB"))
            errors.Add("subclass must be either 'FG' or 'PGB'.");

        // Keep this requirement if you want, but note: we override OutputFolder anyway.
        // If you'd rather let the API force it, you can remove this validation later.
        if (string.IsNullOrWhiteSpace(r.OutputFolder))
            errors.Add("outputFolder is required.");

        if (r.DrawingTypes is { Count: > 0 })
        {
            foreach (var t in r.DrawingTypes)
            {
                if (string.IsNullOrWhiteSpace(t))
                    errors.Add("drawingTypes contains an empty value.");
            }
        }

        return errors;
    }
}

public enum JobStatus { Queued, Running, Succeeded, Failed }

public sealed class JobInfo
{
    public Guid Id { get; init; }
    public JobStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public int ProgressPercent { get; set; }
    public string? Message { get; set; }
    public string? ResultPath { get; set; }   // file or directory
    public string? Error { get; set; }

    public RunRequest? Payload { get; set; }

    // Final normalized list used by executor (multi-type execution)
    public List<string> DrawingTypesNormalized { get; init; } = new();
}

public sealed class JobRequest
{
    public Guid JobId { get; }
    public JobRequest(Guid jobId) => JobId = jobId;
}

public sealed class JobStore
{
    private readonly ConcurrentDictionary<Guid, JobInfo> _jobs = new();

    public void Add(JobInfo job) => _jobs[job.Id] = job;

    public bool TryGet(Guid id, out JobInfo? job) =>
        _jobs.TryGetValue(id, out job);

    public void Update(Guid id, Action<JobInfo> update)
    {
        if (_jobs.TryGetValue(id, out var job))
            update(job);
    }
}

public record ProgressUpdate(int Percent, string Message);

// Executor interface – implemented by SolidWorksAutomationExecutor in another file
public interface IAutomationExecutor
{
    string Execute(JobInfo job, Action<ProgressUpdate> report);
}

// Background STA worker that reads from the channel and calls IAutomationExecutor
public sealed class StaWorkerService : BackgroundService
{
    private readonly ILogger<StaWorkerService> _logger;
    private readonly Channel<JobRequest> _queue;
    private readonly JobStore _store;
    private readonly IAutomationExecutor _executor;

    public StaWorkerService(
        ILogger<StaWorkerService> logger,
        Channel<JobRequest> queue,
        JobStore store,
        IAutomationExecutor executor)
    {
        _logger = logger;
        _queue = queue;
        _store = store;
        _executor = executor;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var thread = new Thread(() => StaLoop(stoppingToken))
        {
            IsBackground = true,
            Name = "SolidWorks-STA-Worker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        _logger.LogInformation("STA worker thread started.");
        return Task.CompletedTask;
    }

    private void StaLoop(CancellationToken ct)
    {
        _logger.LogInformation("STA loop running on thread {ThreadId}", Environment.CurrentManagedThreadId);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_queue.Reader.WaitToReadAsync(ct).AsTask().GetAwaiter().GetResult())
                    continue;

                while (_queue.Reader.TryRead(out var req))
                {
                    if (!_store.TryGet(req.JobId, out var job) || job is null)
                        continue;

                    _store.Update(job.Id, j =>
                    {
                        j.Status = JobStatus.Running;
                        j.StartedUtc = DateTimeOffset.UtcNow;
                        j.ProgressPercent = 0;
                        j.Message = "Starting job…";
                    });

                    try
                    {
                        var resultPath = _executor.Execute(job, progress =>
                        {
                            _store.Update(job.Id, j =>
                            {
                                j.ProgressPercent = Math.Clamp(progress.Percent, 0, 100);
                                j.Message = progress.Message;
                            });
                        });

                        _store.Update(job.Id, j =>
                        {
                            j.Status = JobStatus.Succeeded;
                            j.FinishedUtc = DateTimeOffset.UtcNow;
                            j.ProgressPercent = 100;
                            j.Message = "Done";
                            j.ResultPath = resultPath;
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        _store.Update(job.Id, j =>
                        {
                            j.Status = JobStatus.Failed;
                            j.FinishedUtc = DateTimeOffset.UtcNow;
                            j.Error = "Cancelled";
                            j.Message = "Cancelled";
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error while executing job {JobId}.", job.Id);

                        _store.Update(job.Id, j =>
                        {
                            j.Status = JobStatus.Failed;
                            j.FinishedUtc = DateTimeOffset.UtcNow;
                            j.Error = ex.ToString();
                            j.Message = "Failed";
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in STA loop.");
        }
    }
}

// ---------- Minimal API Host ----------
public static class ApiHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ---------- Configuration ----------
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.AddOptions<RunnerOptions>()
            .Bind(builder.Configuration.GetSection("Runner"))
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Runner:ApiKey is required")
            .ValidateOnStart();

        var runnerOpts = builder.Configuration.GetSection("Runner").Get<RunnerOptions>() ?? new RunnerOptions();

        if (!string.IsNullOrWhiteSpace(runnerOpts.Urls))
            builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, runnerOpts.Urls);

        var jobsRoot = builder.Configuration.GetValue<string>("JobsRoot") ?? @"C:\WedgeJobs";
        Directory.CreateDirectory(jobsRoot);

        builder.Services.AddHttpContextAccessor();

        // ---------- Core services ----------
        builder.Services.AddSingleton<JobStore>();
        builder.Services.AddSingleton(_ => Channel.CreateBounded<JobRequest>(
            new BoundedChannelOptions(runnerOpts.QueueCapacity <= 0 ? 64 : runnerOpts.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            }));

        // ProAlpha repository (SQLite)
        var cs = builder.Configuration.GetConnectionString("ProAlphaSqlite")
                 ?? throw new InvalidOperationException("Missing ConnectionStrings:ProAlphaSqlite for API host.");
        builder.Services.AddSingleton(new ProAlphaRepository(cs));

        var firma = builder.Configuration.GetValue<int>("ProAlpha:Firma", 1);
        var language = builder.Configuration.GetValue<string>("ProAlpha:Language", "E");

        builder.Services.AddSingleton<IWedgeDataSource>(sp =>
            new SqliteWedgeDataSource(
                sp.GetRequiredService<ProAlphaRepository>(),
                firma,
                language,
                sp.GetService<ILogger<SqliteWedgeDataSource>>()
            )
        );

        // Drawing config (JSON)
        var drawingCfgPath = builder.Configuration["DrawingConfig:Path"] ?? "Infrastructure/Config/drawing_config.json";
        builder.Services.AddSingleton<IDrawingDataSource>(_ => new JsonDrawingDataSource(drawingCfgPath));

        // Use-cases
        builder.Services.AddTransient<GetWedgeData>();
        builder.Services.AddTransient<GetDrawingData>();
        builder.Services.AddTransient<BuildAnnotationSet>();
        builder.Services.AddTransient<PlanDrawing>();

        // SolidWorks session factory
        builder.Services.AddSingleton<ISwSessionFactory, SwServiceFactory>();

        // Part automation
        builder.Services.AddSingleton<IPartAutomationService, PartAutomationService>();
        builder.Services.AddSingleton<PartAutomationOrchestrator>();

        // Automation executor + STA worker
        builder.Services.AddSingleton<IAutomationExecutor, SolidWorksAutomationExecutor>();
        builder.Services.AddHostedService<StaWorkerService>();

        var app = builder.Build();

        // ---------- API Key middleware ----------
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            var opts = ctx.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<RunnerOptions>>()
                .Value;

            if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var provided) ||
                provided != opts.ApiKey)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                return;
            }

            await next();
        });

        // ---------- Endpoints ----------

        app.MapGet("/health", () => Results.Ok(new { ok = true, ts = DateTimeOffset.UtcNow }));

        app.MapPost("/run", async (
            HttpContext ctx,
            RunRequest body,
            JobStore store,
            Channel<JobRequest> queue,
            ILoggerFactory lf,
            IConfiguration cfg) =>
        {
            var logger = lf.CreateLogger("RunEndpoint");

            var errors = RunRequestValidator.Validate(body);
            if (errors.Count > 0)
                return Results.BadRequest(new { errors });

            // IMPORTANT: normalize drawing types with legacy support:
            // 1) body.DrawingTypes
            // 2) body.Options["drawingTypes"]   (your web app)
            // 3) body.DrawingType
            var drawingTypes = NormalizeDrawingTypes(body);

            var jobsRootLocal = cfg.GetValue<string>("JobsRoot") ?? @"C:\WedgeJobs";
            var id = Guid.NewGuid();
            var jobDir = Path.Combine(jobsRootLocal, id.ToString("N"));
            var resultsDir = Path.Combine(jobDir, "results");
            Directory.CreateDirectory(resultsDir);

            // Force OutputFolder into per-job results directory
            var payload = new RunRequest
            {
                ArticleNumbers = body.ArticleNumbers,
                DrawingTypes = body.DrawingTypes,
                DrawingType = body.DrawingType,
                Subclass = body.Subclass,
                OutputFolder = resultsDir,
                Options = body.Options
            };

            var job = new JobInfo
            {
                Id = id,
                CreatedUtc = DateTimeOffset.UtcNow,
                Status = JobStatus.Queued,
                Payload = payload,
                DrawingTypesNormalized = drawingTypes
            };

            store.Add(job);

            if (!queue.Writer.TryWrite(new JobRequest(id)))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            logger.LogInformation(
                "Enqueued job {JobId} for {Count} article(s) [{Subclass}] types=[{Types}] out='{Out}'",
                id,
                payload.ArticleNumbers?.Count ?? 0,
                payload.Subclass,
                string.Join(",", drawingTypes),
                payload.OutputFolder);

            return Results.Ok(new { jobId = id });
        });

        app.MapGet("/jobs/{id:guid}", (Guid id, JobStore store) =>
        {
            if (!store.TryGet(id, out var job))
                return Results.NotFound(new { error = "Job not found" });

            return Results.Ok(new
            {
                job.Id,
                job.Status,
                job.CreatedUtc,
                job.StartedUtc,
                job.FinishedUtc,
                job.ProgressPercent,
                job.Message,
                job.ResultPath,
                job.Error
            });
        });

        app.MapGet("/jobs/{id:guid}/download", (Guid id, JobStore store) =>
        {
            if (!store.TryGet(id, out var job))
                return Results.NotFound(new { error = "Job not found" });

            if (job.Status != JobStatus.Succeeded)
                return Results.BadRequest(new { error = $"Job is not completed (status={job.Status})" });

            var resultsPath = job.Payload?.OutputFolder;
            if (string.IsNullOrWhiteSpace(resultsPath) || !Directory.Exists(resultsPath))
                return Results.NotFound(new { error = "Results folder not found." });

            var tmp = Path.GetTempFileName();
            var zipPath = Path.ChangeExtension(tmp, ".zip");
            File.Move(tmp, zipPath, overwrite: true);

            ZipFile.CreateFromDirectory(resultsPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            var fileName = $"job-{id:N}.zip";
            var stream = File.OpenRead(zipPath);
            return Results.File(stream, "application/zip", fileDownloadName: fileName, enableRangeProcessing: true);
        });

        app.MapGet("/specs/{firma:int}/{article}", async (
            int firma,
            string article,
            string? subclass,
            ProAlphaRepository repo) =>
        {
            var partSpec = await repo.GetPartSpecAsync(firma, article, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(partSpec))
                return Results.NotFound(new { error = "PartSpec not found for article", firma, article });

            var spec1Rows = await repo.GetRowsAsync(firma, partSpec, "Wed-Spec1", CancellationToken.None);
            var spec2Rows = await repo.GetRowsAsync(firma, partSpec, "Wed-Spec2", CancellationToken.None);

            var spec1 = spec1Rows
                .Select(r => new { Template = "Wed-Spec1", XRow = r.XRow, ColumnId = r.Payload })
                .ToList();

            var spec2 = spec2Rows
                .Select(r => new { Template = "Wed-Spec2", XRow = r.XRow, ColumnId = r.Payload })
                .ToList();

            var all = spec1.Concat(spec2).ToList();

            return Results.Ok(new
            {
                firma,
                article,
                subclass = (subclass ?? "FG").ToUpperInvariant(),
                partSpec,
                spec1,
                spec2,
                all
            });
        });

        await app.RunAsync();
    }

    private static List<string> NormalizeDrawingTypes(RunRequest body)
    {
        // 1) New style
        if (body.DrawingTypes is { Count: > 0 })
            return body.DrawingTypes
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        // 2) Legacy style: Options["drawingTypes"]="Production,Customer,Overlay"
        if (body.Options is not null &&
            body.Options.TryGetValue("drawingTypes", out var csv) &&
            !string.IsNullOrWhiteSpace(csv))
        {
            return csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // 3) Fallback
        var single = string.IsNullOrWhiteSpace(body.DrawingType) ? "Production" : body.DrawingType!;
        return new List<string> { single.Trim() };
    }
}

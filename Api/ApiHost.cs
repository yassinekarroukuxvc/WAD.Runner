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

using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.SolidWorks;

using WAD.Runner.Application.Ports;
using WAD.Runner.Application.UseCases;

using WAD.Runner.DataManagement.Infrastructure.Serialization;
using WAD.Runner.DataManagement.Infrastructure.Adapters;
using WAD.Runner.DataManagement.Infrastructure.Sqlite;
using WAD.Runner.DataManagement.Infrastructure.Transport;
using WAD.Runner.DataManagement.Domain.Validation;

using WAD.Runner.SolidWorks.Adapters;
using WAD.Runner.Solidworks.Adapters;

namespace WAD.Runner.Api;

public sealed class RunnerOptions
{
    public string? Urls { get; set; }

    [Required]
    public string ApiKey { get; set; } = "dev-key-change-me";

    public int QueueCapacity { get; set; } = 64;
}

public record RunRequest
{
    public List<string>? ArticleNumbers { get; init; }

    public List<string>? DrawingTypes { get; init; }

    public string? DrawingType { get; init; } = "Production";

    public string Subclass { get; init; } = "FG";

    public string? OutputFolder { get; init; }

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
    public string? ResultPath { get; set; }
    public string? Error { get; set; }

    public RunRequest? Payload { get; set; }

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

public interface IAutomationExecutor
{
    string Execute(JobInfo job, Action<ProgressUpdate> report);
}

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
                    catch (WedgeDimensionValidationException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Dimension validation failed before executing job {JobId}.",
                            job.Id);

                        _store.Update(job.Id, j =>
                        {
                            j.Status = JobStatus.Failed;
                            j.FinishedUtc = DateTimeOffset.UtcNow;
                            j.Error = ex.Message;
                            j.Message = "Dimension validation failed";
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

public static class ApiHost
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.AddOptions<RunnerOptions>()
            .Bind(builder.Configuration.GetSection("Runner"))
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Runner:ApiKey is required")
            .ValidateOnStart();

        builder.Services.AddSingleton<ModelDimensionApplier>();
        builder.Services.AddSingleton<ModelAutomationOrchestrator>();

        var runnerOpts = builder.Configuration.GetSection("Runner").Get<RunnerOptions>() ?? new RunnerOptions();

        if (!string.IsNullOrWhiteSpace(runnerOpts.Urls))
            builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, runnerOpts.Urls);

        var jobsRoot = builder.Configuration.GetValue<string>("JobsRoot") ?? @"C:\WedgeJobs";
        Directory.CreateDirectory(jobsRoot);

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddSingleton<JobStore>();
        builder.Services.AddSingleton(_ => Channel.CreateBounded<JobRequest>(
            new BoundedChannelOptions(runnerOpts.QueueCapacity <= 0 ? 64 : runnerOpts.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            }));

        var useJava = builder.Configuration.GetValue<bool>("Runner:UseJavaDbApi", false);

        if (useJava)
        {
            var baseUrl = builder.Configuration.GetValue<string>("Runner:JavaDbApi:BaseUrl")
                          ?? throw new InvalidOperationException("Missing Runner:JavaDbApi:BaseUrl");

            var timeoutSec = builder.Configuration.GetValue<int?>("Runner:JavaDbApi:TimeoutSeconds") ?? 45;
            var apiKey = builder.Configuration.GetValue<string>("Runner:JavaDbApi:ApiKey");

            var firma = builder.Configuration.GetValue<int?>("ProAlpha:Firma") ?? 200;
            var language = builder.Configuration.GetValue<string>("ProAlpha:Language", "E");

            builder.Services.AddHttpClient("JavaLegacyWedgeTransport", http =>
            {
                http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                http.Timeout = TimeSpan.FromSeconds(timeoutSec);

                if (!http.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                if (!string.IsNullOrWhiteSpace(apiKey))
                    http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            });

            builder.Services.AddSingleton<IJavaWedgeTransport>(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var http = factory.CreateClient("JavaLegacyWedgeTransport");
                return new JavaLegacyWedgeTransport(http, firma, language);
            });

            builder.Services.AddSingleton<IWedgeDataSource, JavaWedgeDataSource>();
        }
        else
        {
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
        }

        var drawingCfgPath = builder.Configuration["DrawingConfig:Path"] ?? "Infrastructure/Config/drawing_config.json";
        builder.Services.AddSingleton<IDrawingDataSource>(_ => new JsonDrawingDataSource(drawingCfgPath));

        builder.Services.AddTransient<GetWedgeData>();
        builder.Services.AddTransient<GetDrawingData>();
        builder.Services.AddTransient<BuildAnnotationSet>();
        builder.Services.AddTransient<PlanDrawing>();

        builder.Services.AddSingleton<ISwSessionFactory, SwServiceFactory>();

        builder.Services.AddSingleton<IAutomationExecutor, SolidWorksAutomationExecutor>();
        builder.Services.AddHostedService<StaWorkerService>();

        var app = builder.Build();

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

            var drawingTypes = NormalizeDrawingTypes(body);

            var jobsRootLocal = cfg.GetValue<string>("JobsRoot") ?? @"C:\WedgeJobs";
            var id = Guid.NewGuid();
            var jobDir = Path.Combine(jobsRootLocal, id.ToString("N"));
            var resultsDir = Path.Combine(jobDir, "results");
            Directory.CreateDirectory(resultsDir);

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

        app.MapGet("/jobs/{id:guid}/download", (Guid id, JobStore store, HttpContext httpContext) =>
        {
            if (!store.TryGet(id, out var job))
                return Results.NotFound(new { error = "Job not found" });

            if (job.Status != JobStatus.Succeeded)
                return Results.BadRequest(new { error = $"Job is not completed (status={job.Status})" });

            var resultsPath = job.Payload?.OutputFolder;
            if (string.IsNullOrWhiteSpace(resultsPath) || !Directory.Exists(resultsPath))
                return Results.NotFound(new { error = "Results folder not found." });

            var zipPath = Path.Combine(
                Path.GetTempPath(),
                $"job-{id:N}-{Guid.NewGuid():N}.zip");

            ZipFile.CreateFromDirectory(
                resultsPath,
                zipPath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            httpContext.Response.OnCompleted(() =>
            {
                try
                {
                    if (System.IO.File.Exists(zipPath))
                        System.IO.File.Delete(zipPath);
                }
                catch
                {
                }

                return Task.CompletedTask;
            });

            var fileName = $"job-{id:N}.zip";
            var stream = System.IO.File.OpenRead(zipPath);

            return Results.File(
                stream,
                "application/zip",
                fileDownloadName: fileName,
                enableRangeProcessing: true);
        });

        app.MapGet("/specs/{firma:int}/{article}", async (
            int firma,
            string article,
            string? subclass,
            IServiceProvider sp,
            IConfiguration cfg) =>
        {
            var subclassNorm = (subclass ?? "FG").Trim().ToUpperInvariant();
            var useJavaLocal = cfg.GetValue<bool>("Runner:UseJavaDbApi", false);

            if (useJavaLocal)
            {
                var api = sp.GetRequiredService<IJavaWedgeTransport>();

                if (subclassNorm == "PGB")
                {
                    var spec1Dto = await api.GetPgbSpec1Async(article, CancellationToken.None);
                    var spec2Dto = await api.GetPgbSpec2Async(article, CancellationToken.None);

                    var spec1 = new List<object?>()
                    {
                        new { Template = "PGB-Spec1", XRow = "PGB-Polish",      ColumnId = spec1Dto.Polish },
                        new { Template = "PGB-Spec1", XRow = "PGB-PS",          ColumnId = spec1Dto.PS },
                        new { Template = "PGB-Spec1", XRow = "PGB-Remarks",     ColumnId = spec1Dto.Remarks },
                        new { Template = "PGB-Spec1", XRow = "Wed-Engrave",     ColumnId = spec1Dto.Engrave },
                        new { Template = "PGB-Spec1", XRow = "Wed-FL-Blank",    ColumnId = spec1Dto.FLBlank },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text1",   ColumnId = spec1Dto.DwgText1 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text2",   ColumnId = spec1Dto.DwgText2 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text3",   ColumnId = spec1Dto.DwgText3 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text4",   ColumnId = spec1Dto.DwgText4 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text5",   ColumnId = spec1Dto.DwgText5 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text6",   ColumnId = spec1Dto.DwgText6 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Dwg-Text7",   ColumnId = spec1Dto.DwgText7 },
                        new { Template = "PGB-Spec1", XRow = "Wed-Type",        ColumnId = spec1Dto.WedType },
                        new { Template = "PGB-Spec1", XRow = "Wed-Foot_Option", ColumnId = spec1Dto.WedFootOption },
                        new { Template = "PGB-Spec1", XRow = "Wed-Wire_Exit",   ColumnId = spec1Dto.WedWireExit },
                        new { Template = "PGB-Spec1", XRow = "Wed-Feed_H/Slot", ColumnId = spec1Dto.WedFeedHSlot },
                        new { Template = "PGB-Spec1", XRow = "PGB-FG-Style", ColumnId = spec1Dto.PgbFgStyle },
                    }
                    .Where(x => x?.GetType().GetProperty("ColumnId")?.GetValue(x) is not null)
                    .ToList();

                    var spec2 = spec2Dto
                        .Select(r => new { Template = "PGB-Spec2", XRow = r.Key, ColumnId = r.Payload })
                        .ToList();

                    var all = spec1.Cast<object>().Concat(spec2).ToList();

                    return Results.Ok(new
                    {
                        firma,
                        article,
                        subclass = subclassNorm,
                        partSpec = "(via Java API)",
                        spec1,
                        spec2,
                        all
                    });
                }
                else
                {
                    var spec1Dto = await api.GetWedSpec1Async(article, CancellationToken.None);
                    var spec2Dto = await api.GetWedSpec2Async(article, CancellationToken.None);
                    var kvalueDto = await api.GetWedKValueAsync(article, CancellationToken.None);
                    var markingDto = await api.GetWedMarkingAsync(article, CancellationToken.None);
                    var description = await api.GetArticleDescriptionAsync(article, CancellationToken.None);

                    var spec1 = new List<object?>()
                    {
                        new { Template = "Wed-Spec1", XRow = "Wed-Polish",      ColumnId = spec1Dto.WedPolish },
                        new { Template = "Wed-Spec1", XRow = "Wed-PS",          ColumnId = spec1Dto.WedPS },
                        new { Template = "Wed-Spec1", XRow = "Wed-Notes",       ColumnId = spec1Dto.WedNotes },
                        new { Template = "Wed-Spec1", XRow = "Wed-Overlay",     ColumnId = spec1Dto.WedOverlay },
                        new { Template = "Wed-Spec1", XRow = "Wed-Engrave",     ColumnId = spec1Dto.WedEngrave },
                        new { Template = "Wed-Spec1", XRow = "Wed-Coining",     ColumnId = spec1Dto.WedCoining },
                        new { Template = "Wed-Spec1", XRow = "Wed-Type",        ColumnId = spec1Dto.WedType },
                        new { Template = "Wed-Spec1", XRow = "Wed-Foot_Option", ColumnId = spec1Dto.WedFootOption },
                        new { Template = "Wed-Spec1", XRow = "Wed-Wire_Exit",   ColumnId = spec1Dto.WedWireExit },
                        new { Template = "Wed-Spec1", XRow = "Wed-Feed_H/Slot", ColumnId = spec1Dto.WedFeedHSlot },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text1",   ColumnId = spec1Dto.DwgText1 },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text2",   ColumnId = spec1Dto.DwgText2 },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text3",   ColumnId = spec1Dto.DwgText3 },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text4",   ColumnId = spec1Dto.DwgText4 },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text5",   ColumnId = spec1Dto.DwgText5 },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text6",   ColumnId = spec1Dto.DwgText6 },
                        new { Template = "Wed-Spec1", XRow = "Wed-Dwg-Text7",   ColumnId = spec1Dto.DwgText7 },
                        new { Template = "Article",   XRow = "Description",     ColumnId = description },
                        new { Template = "Wed-Spec1", XRow = "Wed-FG-Style", ColumnId = spec1Dto.WedFgStyle },
                    }
                    .Where(x => x?.GetType().GetProperty("ColumnId")?.GetValue(x) is not null)
                    .ToList();

                    var spec2 = spec2Dto
                        .Select(r => new { Template = "Wed-Spec2", XRow = r.Key, ColumnId = r.Payload })
                        .ToList();

                    var kvaluePayload = TryGetWedKValuePayload(kvalueDto);
                    if (!string.IsNullOrWhiteSpace(kvaluePayload))
                    {
                        spec2.Add(new { Template = "Wed-Spec2", XRow = "Wed_K-Value", ColumnId = kvaluePayload });
                    }

                    var marking = markingDto
                        .Select(r => new { Template = "Wed-Marking", XRow = r.XRow, ColumnId = r.Text })
                        .ToList();

                    var all = spec1.Cast<object>().Concat(spec2).Concat(marking).ToList();

                    return Results.Ok(new
                    {
                        firma,
                        article,
                        subclass = subclassNorm,
                        partSpec = "(via Java API)",
                        spec1,
                        spec2,
                        marking,
                        all
                    });
                }
            }

            var repo = sp.GetService<ProAlphaRepository>();
            if (repo is null)
            {
                return Results.BadRequest(new
                {
                    error = "ProAlphaRepository is not registered."
                });
            }

            var partSpec = await repo.GetPartSpecAsync(firma, article, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(partSpec))
                return Results.NotFound(new { error = "PartSpec not found for article", firma, article });

            if (subclassNorm == "PGB")
            {
                var spec1Rows = await repo.GetRowsAsync(firma, partSpec, "PGB-Spec1", CancellationToken.None);
                var spec2Rows = await repo.GetRowsAsync(firma, partSpec, "PGB-Spec2", CancellationToken.None);

                var spec1 = spec1Rows
                    .Select(r => new { Template = "PGB-Spec1", XRow = r.XRow, ColumnId = r.Payload })
                    .ToList();

                var spec2 = spec2Rows
                    .Select(r => new { Template = "PGB-Spec2", XRow = r.XRow, ColumnId = r.Payload })
                    .ToList();

                var all = spec1.Concat(spec2).ToList();

                return Results.Ok(new
                {
                    firma,
                    article,
                    subclass = subclassNorm,
                    partSpec,
                    spec1,
                    spec2,
                    all
                });
            }
            else
            {
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
                    subclass = subclassNorm,
                    partSpec,
                    spec1,
                    spec2,
                    all
                });
            }
        });

        await app.RunAsync();
    }

    private static List<string> NormalizeDrawingTypes(RunRequest body)
    {
        if (body.DrawingTypes is { Count: > 0 })
            return body.DrawingTypes
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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

        var single = string.IsNullOrWhiteSpace(body.DrawingType) ? "Production" : body.DrawingType!;
        return new List<string> { single.Trim() };
    }

    private static string? TryGetWedKValuePayload(object? dto)
    {
        if (dto is null) return null;

        try
        {
            var t = dto.GetType();

            foreach (var name in new[] { "Raw", "Value", "Payload", "KValue", "Text" })
            {
                var p = t.GetProperty(name);
                if (p?.GetValue(dto) is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }

            var props = t.GetProperties()
                .Where(p => p.PropertyType == typeof(string))
                .ToArray();

            if (props.Length == 1)
            {
                if (props[0].GetValue(dto) is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }
        catch
        {
        }

        return null;
    }
}

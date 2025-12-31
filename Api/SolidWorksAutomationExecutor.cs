// WAD.Runner.Api/SolidWorksAutomationExecutor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using WAD.Runner.Application.UseCases;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.PartAutomation.Common;    // PathPlanner
using WAD.Runner.PartAutomation.Execution;
using WAD.Runner.PartAutomation.Jobs;

using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Executors.FG;
using WAD.Runner.DrawingAutomation.Executors.PGB;

using WAD.Runner.Solidworks.Adapters;

namespace WAD.Runner.Api;

public sealed class SolidWorksAutomationExecutor : IAutomationExecutor
{
    private readonly ILogger<SolidWorksAutomationExecutor> _logger;
    private readonly IServiceProvider _services;

    public SolidWorksAutomationExecutor(
        ILogger<SolidWorksAutomationExecutor> logger,
        IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    public string Execute(JobInfo job, Action<ProgressUpdate> report)
    {
        if (job.Payload is null)
            throw new InvalidOperationException("Job payload is null.");

        var payload = job.Payload;

        if (payload.ArticleNumbers is null || payload.ArticleNumbers.Count == 0)
            throw new InvalidOperationException("Job payload has no ArticleNumbers.");

        // Subclass: "FG" / "PGB"
        var subclassStr = (payload.Subclass ?? "FG").Trim();
        if (!Enum.TryParse<WedgeSubclass>(subclassStr, true, out var subclass))
            subclass = WedgeSubclass.FG;

        // Drawing types: prefer normalized list, else legacy Options["drawingTypes"], else DrawingType
        var drawingTypeNames = ResolveDrawingTypes(job, payload);

        // WedgeType from options["wedgeType"], default CKVD
        var wedgeTypeStr = payload.Options is not null &&
                           payload.Options.TryGetValue("wedgeType", out var wtype)
            ? wtype
            : "CKVD";

        wedgeTypeStr = (wedgeTypeStr ?? "CKVD").Trim().ToUpperInvariant();
        var wedgeType = wedgeTypeStr switch
        {
            "COB" => WedgeType.COB,
            _ => WedgeType.CKVD
        };

        // Job-specific output root (API sets this to C:\WedgeJobs\<jobId>\results)
        var outputRootBase = payload.OutputFolder ?? Path.Combine("Resources", "Out");
        Directory.CreateDirectory(outputRootBase);

        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        var getWedge = sp.GetRequiredService<GetWedgeData>();
        var getDrawing = sp.GetRequiredService<GetDrawingData>();
        var orchestrator = sp.GetRequiredService<PartAutomationOrchestrator>();
        var sessFactory = sp.GetRequiredService<ISwSessionFactory>();

        // Progress accounting:
        // We report 3 times per (article,dtype): load + part + drawing.
        const int StepsPerRun = 3;
        var totalRuns = payload.ArticleNumbers.Count * drawingTypeNames.Count;
        var totalSteps = totalRuns * StepsPerRun;
        var doneSteps = 0;

        string? lastResultPdf = null;

        foreach (var article in payload.ArticleNumbers)
        {
            foreach (var dtypeName in drawingTypeNames)
            {
                if (!Enum.TryParse<DrawingType>(dtypeName, true, out var dtype))
                    dtype = DrawingType.Production;

                _logger.LogInformation(
                    "Job {JobId}: starting automation for article={Article}, subclass={Subclass}, type={Type}, wtype={WType}",
                    job.Id, article, subclass, dtype, wedgeType);

                // 1) Load domain data
                report(Progress(++doneSteps, totalSteps, $"Loading data for {article} ({dtype})…"));

                var wedgeData = getWedge.ExecuteAsync(article, subclass, CancellationToken.None)
                                        .GetAwaiter().GetResult();

                var drawingData = getDrawing.ExecuteAsync(dtype, subclass, wedgeType, article, CancellationToken.None)
                                            .GetAwaiter().GetResult();

                // 2) Select templates
                string templatePartPath;
                string templateDrawingPath;
                string equationTemplatePathForPartPhase;

                switch (wedgeType)
                {
                    case WedgeType.COB:
                        templatePartPath = Path.Combine("Resources", "Templates", "COB", "COB_Template.SLDPRT");
                        templateDrawingPath = Path.Combine("Resources", "Templates", "COB", "COB_Drawings.SLDDRW");
                        equationTemplatePathForPartPhase = Path.Combine("Resources", "Templates", "COB", "equations.txt");
                        break;

                    case WedgeType.CKVD:
                    default:
                        templatePartPath = Path.Combine("Resources", "Templates", "CKVD", "CKVD_2023.SLDPRT");
                        templateDrawingPath = dtype switch
                        {
                            DrawingType.Overlay =>
                                Path.Combine("Resources", "Templates", "CKVD", "OVERLAY_TEMPLATE.SLDDRW"),

                            DrawingType.Production or DrawingType.Customer or _ =>
                                Path.Combine("Resources", "Templates", "CKVD", "CKVD_2023.SLDDRW"),
                        };
                        equationTemplatePathForPartPhase = Path.Combine("Resources", "Templates", "CKVD", "CK.txt");
                        break;
                }

                // 3) Plan paths consistently (suffix-aware)
                var plan = PathPlanner.Build(
                    article: article,
                    subclass: subclass,
                    drawingType: dtype,
                    outputRoot: outputRootBase,
                    fileBase: null);

                var modDrawingPath = Path.Combine(plan.WorkDir, $"{plan.FileBase}.SLDDRW");

                var run = new DrawingRun
                {
                    WedgeType = wedgeType,

                    TemplatePartPath = templatePartPath,
                    TemplateDrawingPath = templateDrawingPath,

                    ModPartPath = plan.PartPath,
                    ModDrawingPath = modDrawingPath,
                    EquationsPath = plan.EquationsPath,

                    Wedge = wedgeData,

                    OutputPdfPath = plan.PdfPath,
                    OutputTiffPath = null
                };

                // 4) Part phase
                report(Progress(++doneSteps, totalSteps, $"Running part phase for {article} ({dtype})…"));

                string? partResultPath;
                using (var swPart = sessFactory.Create(visible: true))
                {
                    var jobReq = new PartJobRequest
                    {
                        ArticleNumber = article,
                        Subclass = subclass,
                        DrawingType = dtype,
                        OutputRoot = outputRootBase,

                        PartTemplatePath = templatePartPath,
                        EquationTemplatePath = equationTemplatePathForPartPhase,

                        // Critical: force same file base that the drawing phase will expect
                        FileBase = plan.FileBase,

                        WedgeData = wedgeData,
                        WedgeType = wedgeType
                    };

                    partResultPath = orchestrator.RunAsync(jobReq, swPart.App, CancellationToken.None)
                                                 .GetAwaiter().GetResult();
                }

                // 5) Drawing phase
                report(Progress(++doneSteps, totalSteps, $"Running drawing phase for {article} ({dtype})…"));

                using (var swDraw = sessFactory.Create(visible: true))
                {
                    Func<object?> runPartAutomation = () => partResultPath;

                    switch (subclass)
                    {
                        case WedgeSubclass.PGB:
                            switch (dtype)
                            {
                                case DrawingType.Customer:
                                    _logger.LogInformation("Job {JobId}: PGB Customer → FG Customer executor (temporary).", job.Id);
                                    FgCustomerDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                    break;

                                case DrawingType.Overlay:
                                    _logger.LogInformation("Job {JobId}: PGB Overlay → PGB Overlay executor.", job.Id);
                                    PgbOverlayDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                    break;

                                case DrawingType.Production:
                                default:
                                    _logger.LogInformation("Job {JobId}: PGB Production → PGB Production executor.", job.Id);
                                    PgbProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                    break;
                            }
                            break;

                        case WedgeSubclass.FG:
                        default:
                            switch (dtype)
                            {
                                case DrawingType.Customer:
                                    _logger.LogInformation("Job {JobId}: FG Customer → FG Customer executor.", job.Id);
                                    FgCustomerDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                    break;

                                case DrawingType.Overlay:
                                    _logger.LogInformation("Job {JobId}: FG Overlay → FG Overlay executor.", job.Id);
                                    FgOverlayDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                    break;

                                case DrawingType.Production:
                                default:
                                    _logger.LogInformation("Job {JobId}: FG Production → FG Production executor.", job.Id);
                                    FgProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                    break;
                            }
                            break;
                    }
                }

                lastResultPdf = run.OutputPdfPath;
                _logger.LogInformation(
                    "Job {JobId}: completed article={Article}, type={Type}, output='{Out}'",
                    job.Id, article, dtype, lastResultPdf);
            }
        }

        report(new ProgressUpdate(100, "All automation completed."));
        return lastResultPdf ?? outputRootBase;
    }

    private static List<string> ResolveDrawingTypes(JobInfo job, RunRequest payload)
    {
        // 1) Already normalized by API (preferred)
        if (job.DrawingTypesNormalized is { Count: > 0 })
            return job.DrawingTypesNormalized;

        // 2) Legacy: Options["drawingTypes"]
        if (payload.Options is not null &&
            payload.Options.TryGetValue("drawingTypes", out var csv) &&
            !string.IsNullOrWhiteSpace(csv))
        {
            var parsed = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parsed.Count > 0)
                return parsed;
        }

        // 3) Fallback
        return new List<string> { (payload.DrawingType ?? "Production").Trim() };
    }

    private static ProgressUpdate Progress(int step, int total, string message)
    {
        var percent = total <= 0
            ? 0
            : (int)Math.Clamp(Math.Round(step * 100.0 / total), 0, 100);

        return new ProgressUpdate(percent, message);
    }
}

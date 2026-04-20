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
using WAD.Runner.DataManagement.Infrastructure.Parsing;

using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Executors.FG;
using WAD.Runner.DrawingAutomation.Executors.PGB;

using WAD.Runner.Solidworks.Adapters;

// ✅ Use ModelAutomation (same as Program.cs)
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Executors;

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

        // -----------------------------
        // Parse subclass
        // -----------------------------
        var subclassStr = (payload.Subclass ?? "FG").Trim();
        if (!Enum.TryParse<WedgeSubclass>(subclassStr, true, out var subclass))
            subclass = WedgeSubclass.FG;

        // -----------------------------
        // Drawing types to run
        // -----------------------------
        var drawingTypeNames = ResolveDrawingTypes(job, payload);

        // -----------------------------
        // Output root
        // -----------------------------
        var outputRootBase = payload.OutputFolder ?? Path.Combine("Resources", "Out");
        Directory.CreateDirectory(outputRootBase);

        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        var getWedge = sp.GetRequiredService<GetWedgeData>();
        var getDrawing = sp.GetRequiredService<GetDrawingData>();
        var modelOrchestrator = sp.GetRequiredService<ModelAutomationOrchestrator>();
        var sessFactory = sp.GetRequiredService<ISwSessionFactory>();

        // Steps:
        //  1) Load data
        //  2) Model phase (ModelAutomation)
        //  3) Drawing phase
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

                // ---------------------------------
                // Step 1) Load data
                // ---------------------------------
                report(Progress(++doneSteps, totalSteps, $"Loading data for {article} ({dtype})…"));

                var wedgeData = getWedge.ExecuteAsync(article, subclass, CancellationToken.None)
                                        .GetAwaiter().GetResult();

                var wedgeType = ResolveWedgeType(wedgeData, payload);

                _logger.LogInformation(
                    "Job {JobId}: starting automation for article={Article}, subclass={Subclass}, type={Type}, wtype={WType}",
                    job.Id, article, subclass, dtype, wedgeType);

                var drawingData = getDrawing.ExecuteAsync(dtype, subclass, wedgeType, article, CancellationToken.None)
                                            .GetAwaiter().GetResult();

                // ---------------------------------
                // Templates (match Program.cs run-drawing)
                // ---------------------------------
                string templatePartPath;
                string templateDrawingPath;
                string equationTemplatePathForModelPhase;

                switch (wedgeType)
                {
                    case WedgeType.COB:
                        templatePartPath = Path.Combine(
                            "Resources", "Templates", "COB", "COB template 02-14-2026", "V4",
                            "wedge-auto-draw-COB-3d-model_sw_version_2023.SLDPRT");

                        templateDrawingPath = dtype switch
                        {
                            DrawingType.Overlay =>
                                Path.Combine(
                                    "Resources", "Templates", "COB", "COB template 02-14-2026", "V4",
                                    "wedge-auto-draw-COB-2d-overlay.SLDDRW"),

                            DrawingType.Production or DrawingType.Customer or _ =>
                                Path.Combine(
                                    "Resources", "Templates", "COB", "COB template 02-14-2026", "V4",
                                    "wedge-auto-draw-COB-2d-drawing.SLDDRW"),
                        };

                        equationTemplatePathForModelPhase = Path.Combine(
                            "Resources", "Templates", "COB", "COB template 02-14-2026", "V4",
                            "wedge-auto-draw-COB-3d-equation.txt");
                        break;

                    case WedgeType.UTUS:
                        templatePartPath = Path.Combine(
                        "Resources", "Templates", "UT-US", "V1",
                        "wedge-auto-draw-UT-US-3d-model_sw_version_2023.SLDPRT");

                        templateDrawingPath = dtype switch
                        {
                            DrawingType.Overlay =>
                                Path.Combine(
                                    "Resources", "Templates", "UT-US", "V1",
                                    "wedge-auto-draw-UT-US-2d-overlay.SLDDRW"),

                            DrawingType.Production or DrawingType.Customer or _ =>
                                Path.Combine(
                                    "Resources", "Templates", "UT-US", "V1",
                                    "wedge-auto-draw-UT-US-2d-drawing.SLDDRW"),
                        };

                        equationTemplatePathForModelPhase = Path.Combine(
                            "Resources", "Templates", "UT-US", "V1",
                            "wedge-auto-draw-UT-US-3d-equation.txt");
                        break;

                    case WedgeType.FP:
                        templatePartPath = Path.Combine(
                            "Resources", "Templates", "FP",
                            "wedge-auto-draw-FP-3d-model_sw_version_2023.SLDPRT");

                        templateDrawingPath = dtype switch
                        {
                            DrawingType.Overlay =>
                                Path.Combine(
                                    "Resources", "Templates", "FP",
                                    "wedge-auto-draw-FP-2d-overlay.SLDDRW"),

                            DrawingType.Production or DrawingType.Customer or _ =>
                                Path.Combine(
                                    "Resources", "Templates", "FP",
                                    "wedge-auto-draw-FP-2d-drawing.SLDDRW"),
                        };

                        equationTemplatePathForModelPhase = Path.Combine(
                            "Resources", "Templates", "FP",
                            "wedge-auto-draw-FP-3d-equation.txt");
                        break;

                    case WedgeType.OSG7:
                        templatePartPath = Path.Combine(
                            "Resources", "Templates", "OSG7",
                            "wedge_auto_draw_OSG7_3d.SLDPRT");

                        templateDrawingPath = dtype switch
                        {
                            DrawingType.Overlay =>
                                Path.Combine(
                                    "Resources", "Templates", "OSG7",
                                    "OSG7_OVERLAY_TEMPLATE.SLDDRW"),

                            DrawingType.Production or DrawingType.Customer or _ =>
                                Path.Combine(
                                    "Resources", "Templates", "OSG7",
                                    "wedge_auto_draw_OSG7_3d.SLDDRW"),
                        };

                        equationTemplatePathForModelPhase = Path.Combine(
                            "Resources", "Templates", "OSG7",
                            "equations_OSG7.txt");
                        break;

                    case WedgeType.CKVD:
                    default:
                        templatePartPath = Path.Combine(
                            "Resources", "Templates", "CKVD", "CKVDv4",
                            "CKVD_2023.SLDPRT");

                        templateDrawingPath = dtype switch
                        {
                            DrawingType.Overlay =>
                                Path.Combine(
                                    "Resources", "Templates", "CKVD", "CKVDv4",
                                    "OVERLAY_TEMPLATE.SLDDRW"),

                            DrawingType.Production or DrawingType.Customer or _ =>
                                Path.Combine(
                                    "Resources", "Templates", "CKVD", "CKVDv4",
                                    "CKVD_2023.SLDDRW"),
                        };

                        equationTemplatePathForModelPhase = Path.Combine(
                            "Resources", "Templates", "CKVD", "CKVDv4",
                            "CK.txt");
                        break;
                }

                // ---------------------------------
                // Plan outputs (use ModelAutomation path planner)
                // ---------------------------------
                var plan = PathPlanner.Build(
                    article: article,
                    subclass: subclass,
                    drawingType: dtype,
                    outputRoot: outputRootBase,
                    fileBase: null
                );

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

                // ---------------------------------
                // Step 2) MODEL phase (ModelAutomation)
                // ---------------------------------
                report(Progress(++doneSteps, totalSteps, $"Running model phase for {article} ({dtype})…"));

                string? modelResultPath;
                using (var swModel = sessFactory.Create(visible: true))
                {
                    var jobReq = new ModelJobRequest
                    {
                        ArticleNumber = article,
                        Subclass = subclass,
                        DrawingType = dtype,
                        OutputRoot = outputRootBase,

                        PartTemplatePath = templatePartPath,
                        EquationTemplatePath = equationTemplatePathForModelPhase,

                        FileBase = plan.FileBase,

                        WedgeData = wedgeData,
                        WedgeType = wedgeType
                    };

                    modelResultPath = modelOrchestrator.RunAsync(jobReq, swModel.App, CancellationToken.None)
                                                     .GetAwaiter().GetResult();
                }

                // ---------------------------------
                // Step 3) DRAWING phase (unchanged)
                // ---------------------------------
                report(Progress(++doneSteps, totalSteps, $"Running drawing phase for {article} ({dtype})…"));

                using (var swDraw = sessFactory.Create(visible: true))
                {
                    Func<object?> runModelAutomation = () => modelResultPath;

                    switch (subclass)
                    {
                        case WedgeSubclass.PGB:
                            switch (dtype)
                            {
                                case DrawingType.Customer:
                                    _logger.LogInformation("Job {JobId}: PGB Customer → FG Customer executor (temporary).", job.Id);
                                    ProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runModelAutomation);
                                    break;

                                case DrawingType.Overlay:
                                    _logger.LogInformation("Job {JobId}: PGB Overlay → PGB Overlay executor.", job.Id);
                                    OverlayDrawingExecutor.Run(swDraw.App, run, drawingData, runModelAutomation, plannedDims: null);
                                    break;

                                case DrawingType.Production:
                                default:
                                    _logger.LogInformation("Job {JobId}: PGB Production → PGB Production executor.", job.Id);
                                    ProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runModelAutomation);
                                    break;
                            }
                            break;

                        case WedgeSubclass.FG:
                        default:
                            switch (dtype)
                            {
                                case DrawingType.Customer:
                                    _logger.LogInformation("Job {JobId}: FG Customer → FG Customer executor.", job.Id);
                                    ProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runModelAutomation);
                                    break;

                                case DrawingType.Overlay:
                                    _logger.LogInformation("Job {JobId}: FG Overlay → FG Overlay executor.", job.Id);
                                    OverlayDrawingExecutor.Run(swDraw.App, run, drawingData, runModelAutomation, plannedDims: null);
                                    break;

                                case DrawingType.Production:
                                default:
                                    _logger.LogInformation("Job {JobId}: FG Production → FG Production executor.", job.Id);
                                    ProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runModelAutomation);
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
        if (job.DrawingTypesNormalized is { Count: > 0 })
            return job.DrawingTypesNormalized;

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

        return new List<string> { (payload.DrawingType ?? "Production").Trim() };
    }

    private static WedgeType ResolveWedgeType(WedgeData wedgeData, RunRequest payload)
    {
        if (TryGetPropertyIgnoreCase(wedgeData, "wedge_type", out var storedType) &&
            WedgeStyleParser.TryParseWedgeType(storedType, out var parsedFromStoredType))
        {
            return parsedFromStoredType;
        }

        if (TryGetPropertyIgnoreCase(wedgeData, "wedge_style", out var storedStyle) &&
            WedgeStyleParser.TryParseWedgeType(storedStyle, out var parsedFromStoredStyle))
        {
            return parsedFromStoredStyle;
        }

        // Temporary fallback during migration from frontend-provided wedge type
        if (payload.Options is not null &&
            payload.Options.TryGetValue("wedgeType", out var oldValue) &&
            WedgeStyleParser.TryParseWedgeType(oldValue, out var parsedFromPayload))
        {
            return parsedFromPayload;
        }

        return WedgeType.CKVD;
    }

    private static bool TryGetPropertyIgnoreCase(WedgeData wedgeData, string key, out string value)
    {
        value = string.Empty;

        if (wedgeData?.Properties is null || wedgeData.Properties.Count == 0)
            return false;

        foreach (var kvp in wedgeData.Properties)
        {
            if (!string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(kvp.Value))
                return false;

            value = kvp.Value.Trim();
            return true;
        }

        return false;
    }

    private static ProgressUpdate Progress(int step, int total, string message)
    {
        var percent = total <= 0
            ? 0
            : (int)Math.Clamp(Math.Round(step * 100.0 / total), 0, 100);

        return new ProgressUpdate(percent, message);
    }
}
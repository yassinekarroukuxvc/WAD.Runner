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
using WAD.Runner.DataManagement.Domain.Validation;
using WAD.Runner.DataManagement.Infrastructure.Parsing;

using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Execution;

using WAD.Runner.Solidworks.Adapters;
using WAD.Runner.SolidWorks.Adapters;

using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.SolidWorks;

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
        var solidWorksTouched = false;

        try
        {
            if (job.Payload is null)
                throw new InvalidOperationException("Job payload is null.");

            var payload = job.Payload;

            if (payload.ArticleNumbers is null || payload.ArticleNumbers.Count == 0)
                throw new InvalidOperationException("Job payload has no ArticleNumbers.");

            var subclassStr = (payload.Subclass ?? "FG").Trim();
            if (!Enum.TryParse<WedgeSubclass>(subclassStr, true, out var subclass))
                subclass = WedgeSubclass.FG;

            var drawingTypeNames = ResolveDrawingTypes(job, payload);

            var outputRootBase = payload.OutputFolder ?? Path.Combine("Resources", "Out");
            Directory.CreateDirectory(outputRootBase);

            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;

            var getWedge = sp.GetRequiredService<GetWedgeData>();
            var getDrawing = sp.GetRequiredService<GetDrawingData>();
            var modelOrchestrator = sp.GetRequiredService<ModelAutomationOrchestrator>();
            var sessFactory = sp.GetRequiredService<ISwSessionFactory>();

            var validatedWedges = LoadAndValidateWedges(
                job,
                payload,
                subclass,
                getWedge,
                report);

            CleanupSolidWorksBeforeJob(job, report);
            solidWorksTouched = true;

            const int StepsPerRun = 3;
            var totalRuns = payload.ArticleNumbers.Count * drawingTypeNames.Count;
            var totalSteps = totalRuns * StepsPerRun;
            var doneSteps = 0;

            string? lastResultPdf = null;

            foreach (var articleRaw in payload.ArticleNumbers)
            {
                var article = articleRaw.Trim();
                var validatedWedge = validatedWedges[article];
                var wedgeData = validatedWedge.WedgeData;
                var wedgeType = validatedWedge.WedgeType;

                foreach (var dtypeName in drawingTypeNames)
                {
                    if (!Enum.TryParse<DrawingType>(dtypeName, true, out var dtype))
                        dtype = DrawingType.Production;

                    report(Progress(++doneSteps, totalSteps, $"Loading drawing data for {article} ({dtype})…"));

                    _logger.LogInformation(
                        "Job {JobId}: starting automation for article={Article}, subclass={Subclass}, type={Type}, wtype={WType}",
                        job.Id,
                        article,
                        subclass,
                        dtype,
                        wedgeType);

                    var drawingData = getDrawing.ExecuteAsync(dtype, subclass, wedgeType, article, CancellationToken.None)
                                                .GetAwaiter()
                                                .GetResult();

                    string templatePartPath;
                    string templateDrawingPath;
                    string equationTemplatePathForModelPhase;

                    switch (wedgeType)
                    {
                        case WedgeType.COB:
                            templatePartPath = Path.Combine(
                                "Resources", "Templates", "COB", "Working Version",
                                "COB.SLDPRT");

                            templateDrawingPath = dtype switch
                            {
                                DrawingType.Overlay =>
                                    Path.Combine(
                                        "Resources", "Templates", "COB", "Working Version",
                                        "COB_Overlay.SLDDRW"),

                                DrawingType.Production or DrawingType.Customer or _ =>
                                    Path.Combine(
                                        "Resources", "Templates", "COB", "Working Version",
                                        "COB_drawings.SLDDRW"),
                            };

                            equationTemplatePathForModelPhase = Path.Combine(
                                "Resources", "Templates", "COB", "Working Version",
                                "equations.txt");
                            break;

                        case WedgeType.UTUS:
                            templatePartPath = Path.Combine(
                                "Resources", "Templates", "UT-US", "Working Version",
                                "COB.SLDPRT");

                            templateDrawingPath = dtype switch
                            {
                                DrawingType.Overlay =>
                                    Path.Combine(
                                        "Resources", "Templates", "UT-US", "Working Version",
                                        "COB_Overlay.SLDDRW"),

                                DrawingType.Production or DrawingType.Customer or _ =>
                                    Path.Combine(
                                        "Resources", "Templates", "UT-US", "Working Version",
                                        "COB_drawings.SLDDRW"),
                            };

                            equationTemplatePathForModelPhase = Path.Combine(
                                "Resources", "Templates", "UT-US", "Working Version",
                                "equations.txt");
                            break;

                        case WedgeType.FP:
                            templatePartPath = Path.Combine(
                                "Resources", "Templates", "FP", "Working Version",
                                "COB.SLDPRT");

                            templateDrawingPath = dtype switch
                            {
                                DrawingType.Overlay =>
                                    Path.Combine(
                                        "Resources", "Templates", "FP", "Working Version",
                                        "COB_Overlay.SLDDRW"),

                                DrawingType.Production or DrawingType.Customer or _ =>
                                    Path.Combine(
                                        "Resources", "Templates", "FP", "Working Version",
                                        "COB_drawings.SLDDRW"),
                            };

                            equationTemplatePathForModelPhase = Path.Combine(
                                "Resources", "Templates", "FP", "Working Version",
                                "equations.txt");
                            break;

                        case WedgeType.OSG7:
                            templatePartPath = Path.Combine(
                                "Resources", "Templates", "OSG7", "Working Version",
                                "OSG7.SLDPRT");

                            templateDrawingPath = dtype switch
                            {
                                DrawingType.Overlay =>
                                    Path.Combine(
                                        "Resources", "Templates", "OSG7", "Working Version",
                                        "OSG7_overlay.SLDDRW"),

                                DrawingType.Production or DrawingType.Customer or _ =>
                                    Path.Combine(
                                        "Resources", "Templates", "OSG7", "Working Version",
                                        "OSG7.SLDDRW"),
                            };

                            equationTemplatePathForModelPhase = Path.Combine(
                                "Resources", "Templates", "OSG7", "Working Version",
                                "equations.txt");
                            break;

                        case WedgeType._4516:
                            templatePartPath = Path.Combine("Resources", "Templates", "4516", "4516_part_rev2.SLDPRT");

                            templateDrawingPath = dtype switch
                            {
                                DrawingType.Overlay =>
                                    Path.Combine("Resources", "Templates", "4516", "4516_overlay_rev2.SLDDRW"),

                                DrawingType.Production or DrawingType.Customer or _ =>
                                    Path.Combine("Resources", "Templates", "4516", "4516_drawing_rev2.SLDPRT.SLDDRW"),
                            };

                            equationTemplatePathForModelPhase = Path.Combine("Resources", "Templates", "4516", "equations.txt");
                            break;

                        case WedgeType.CKVD:
                        default:
                            templatePartPath = Path.Combine("Resources", "Templates", "CKVD", "CKVD_rev2", "ckvd_part_rev2.SLDPRT");
                            templateDrawingPath = dtype switch
                            {
                                DrawingType.Overlay =>
                                    Path.Combine("Resources", "Templates", "CKVD", "CKVD_rev2", "ckvd_overlay_rev2.SLDDRW"),
                                DrawingType.Production or DrawingType.Customer or _ =>
                                    Path.Combine("Resources", "Templates", "CKVD", "CKVD_rev2", "ckvd_drawing_rev2.SLDPRT.SLDDRW"),
                            };
                            equationTemplatePathForModelPhase = Path.Combine("Resources", "Templates", "CKVD", "CKVD_rev2", "equations.txt");
                            break;
                    }

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
                                                          .GetAwaiter()
                                                          .GetResult();
                    }

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
                                        _logger.LogInformation(
                                            "Job {JobId}: PGB Customer → FG Customer executor (temporary).",
                                            job.Id);

                                        DrawingAutomationExecutor.RunProduction(
                                            swDraw.App,
                                            run,
                                            drawingData,
                                            runModelAutomation);
                                        break;

                                    case DrawingType.Overlay:
                                        _logger.LogInformation(
                                            "Job {JobId}: PGB Overlay → PGB Overlay executor.",
                                            job.Id);

                                        DrawingAutomationExecutor.RunOverlay(
                                            swDraw.App,
                                            run,
                                            drawingData,
                                            runModelAutomation,
                                            null);
                                        break;

                                    case DrawingType.Production:
                                    default:
                                        _logger.LogInformation(
                                            "Job {JobId}: PGB Production → PGB Production executor.",
                                            job.Id);

                                        DrawingAutomationExecutor.RunProduction(
                                            swDraw.App,
                                            run,
                                            drawingData,
                                            runModelAutomation);
                                        break;
                                }
                                break;

                            case WedgeSubclass.FG:
                            default:
                                switch (dtype)
                                {
                                    case DrawingType.Customer:
                                        _logger.LogInformation(
                                            "Job {JobId}: FG Customer → FG Customer executor.",
                                            job.Id);

                                        DrawingAutomationExecutor.RunProduction(
                                            swDraw.App,
                                            run,
                                            drawingData,
                                            runModelAutomation);
                                        break;

                                    case DrawingType.Overlay:
                                        _logger.LogInformation(
                                            "Job {JobId}: FG Overlay → FG Overlay executor.",
                                            job.Id);

                                        DrawingAutomationExecutor.RunOverlay(
                                            swDraw.App,
                                            run,
                                            drawingData,
                                            runModelAutomation,
                                            null);
                                        break;

                                    case DrawingType.Production:
                                    default:
                                        _logger.LogInformation(
                                            "Job {JobId}: FG Production → FG Production executor.",
                                            job.Id);

                                        DrawingAutomationExecutor.RunProduction(
                                            swDraw.App,
                                            run,
                                            drawingData,
                                            runModelAutomation);
                                        break;
                                }
                                break;
                        }
                    }

                    lastResultPdf = run.OutputPdfPath;

                    _logger.LogInformation(
                        "Job {JobId}: completed article={Article}, type={Type}, output='{Out}'",
                        job.Id,
                        article,
                        dtype,
                        lastResultPdf);
                }
            }

            report(new ProgressUpdate(100, "All automation completed."));
            return lastResultPdf ?? outputRootBase;
        }
        finally
        {
            if (solidWorksTouched)
                CleanupSolidWorksAfterJob(job);
        }
    }

    private Dictionary<string, ValidatedWedgeData> LoadAndValidateWedges(
        JobInfo job,
        RunRequest payload,
        WedgeSubclass subclass,
        GetWedgeData getWedge,
        Action<ProgressUpdate> report)
    {
        var validated = new Dictionary<string, ValidatedWedgeData>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<DimensionValidationIssue>();
        var shouldValidateDimensions = subclass == WedgeSubclass.FG;

        foreach (var articleRaw in payload.ArticleNumbers!.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var article = articleRaw.Trim();
            report(new ProgressUpdate(0, shouldValidateDimensions
                ? $"Validating dimensions for {article}…"
                : $"Loading wedge data for {article}; dimension validation skipped for PGB…"));

            var wedgeData = getWedge.ExecuteAsync(article, subclass, CancellationToken.None)
                                    .GetAwaiter()
                                    .GetResult();

            var wedgeType = ResolveWedgeType(wedgeData, payload);

            if (shouldValidateDimensions)
            {
                var validationResult = WedgeDimensionValidator.Validate(wedgeData, wedgeType);

                if (!validationResult.IsValid)
                {
                    errors.AddRange(validationResult.Issues);
                    continue;
                }

                _logger.LogInformation(
                    "Job {JobId}: dimension validation passed for article={Article}, subclass={Subclass}, wtype={WType}.",
                    job.Id,
                    article,
                    subclass,
                    wedgeType);
            }
            else
            {
                _logger.LogInformation(
                    "Job {JobId}: dimension validation skipped for article={Article}, subclass={Subclass}, wtype={WType}. Only FG validation rules are active for now.",
                    job.Id,
                    article,
                    subclass,
                    wedgeType);
            }

            validated[article] = new ValidatedWedgeData(wedgeData, wedgeType);
        }

        if (errors.Count > 0)
        {
            var first = errors[0];
            var result = new DimensionValidationResult(first.ArticleNumber, first.WedgeType, errors);
            throw new WedgeDimensionValidationException(result);
        }

        if (validated.Count == 0)
            throw new InvalidOperationException("Job payload does not contain any non-empty article numbers.");

        report(new ProgressUpdate(0, shouldValidateDimensions
            ? "Dimension validation passed. Starting SolidWorks automation…"
            : "Dimension validation skipped for PGB. Starting SolidWorks automation…"));

        return validated;
    }

    private void CleanupSolidWorksBeforeJob(JobInfo job, Action<ProgressUpdate> report)
    {
        _logger.LogInformation(
            "Job {JobId}: cleaning existing SolidWorks processes before automation.",
            job.Id);

        report(new ProgressUpdate(0, "Cleaning existing SolidWorks instances…"));

        try
        {
            SolidWorksProcessKiller.KillAll(killVbaServer: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Job {JobId}: SolidWorks cleanup failed before automation. Continuing job execution.",
                job.Id);
        }
    }

    private void CleanupSolidWorksAfterJob(JobInfo job)
    {
        _logger.LogInformation(
            "Job {JobId}: cleaning SolidWorks processes after automation.",
            job.Id);

        try
        {
            SolidWorksProcessKiller.KillAll(killVbaServer: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Job {JobId}: SolidWorks cleanup failed after automation.",
                job.Id);
        }
    }

    private static List<string> ResolveDrawingTypes(JobInfo job, RunRequest payload)
    {
        if (job.DrawingTypesNormalized is { Count: > 0 })
            return job.DrawingTypesNormalized;

        if (payload.Options is not null &&
            payload.Options.TryGetValue("drawingTypes", out var csv) &&
            !string.IsNullOrWhiteSpace(csv))
        {
            var parsed = csv.Split(
                    new[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parsed.Count > 0)
                return parsed;
        }

        return new List<string> { (payload.DrawingType ?? "Production").Trim() };
    }

    private sealed record ValidatedWedgeData(WedgeData WedgeData, WedgeType WedgeType);

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
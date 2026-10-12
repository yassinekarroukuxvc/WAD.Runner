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

using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.SolidWorks;

namespace WAD.Runner.Api;

public sealed class WedgeTypeResolutionException : InvalidOperationException
{
    public WedgeTypeResolutionException(string message)
        : base(message)
    {
    }
}

public sealed class SolidWorksAutomationExecutor : IAutomationExecutor
{
    private const string SupportedWedgeTypesText =
        "CKVD, COB, UTUS, OSG7, FP, 4516, ABT, AB16, 45CK, M, 1001, 1007, 1300, 1005A";

    private readonly ILogger<SolidWorksAutomationExecutor> _logger;
    private readonly IServiceProvider _services;

    public SolidWorksAutomationExecutor(
        ILogger<SolidWorksAutomationExecutor> logger,
        IServiceProvider services)
    {
        _logger = logger;
        _services = services;
    }

    private bool SolidWorksVisible()
    {
        var cfg = _services.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        return cfg?["Runner:SolidWorks:Visible"] is { } raw &&
               bool.TryParse(raw, out var visible) && visible;
    }

    public string Execute(JobInfo job, Action<ProgressUpdate> report)
    {
        // One owned SolidWorks process per API request, including Production+Overlay.
        SolidWorksSessionManager? sessions = null;

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

            var databaseTarget = JavaDatabaseTargetParser.ParseOrDefault(payload.Database);
            var databaseSelection = sp.GetService<JavaDatabaseSelectionContext>();
            using var databaseScope = databaseSelection?.Push(databaseTarget);

            _logger.LogInformation(
                "Job {JobId}: database selection = {Database} (applies when Runner:UseJavaDbApi=true).",
                job.Id,
                databaseTarget);

            var getWedge = sp.GetRequiredService<GetWedgeData>();
            var getDrawing = sp.GetRequiredService<GetDrawingData>();
            var modelOrchestrator = sp.GetRequiredService<ModelAutomationOrchestrator>();
            sessions = _services.GetRequiredService<SolidWorksSessionManager>();

            var validatedWedges = LoadAndValidateWedges(
                job,
                payload,
                subclass,
                getWedge,
                report);

            // No global process killer here. The session factory refuses to
            // attach to an existing user SolidWorks process.

            const int StepsPerRun = 3;
            var totalRuns = payload.ArticleNumbers.Count * drawingTypeNames.Count;
            var totalSteps = totalRuns * StepsPerRun;
            var doneSteps = 0;

            string? lastResultArtifact = null;

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
                        "Job {JobId}: starting automation for article={Article}, subclass={Subclass}, database={Database}, type={Type}, wtype={WType}",
                        job.Id,
                        article,
                        subclass,
                        databaseTarget,
                        dtype,
                        wedgeType);

                    var drawingData = getDrawing.ExecuteAsync(dtype, subclass, wedgeType, article, CancellationToken.None)
                                                .GetAwaiter()
                                                .GetResult();

                    if (!WedgeTemplateCatalog.TryGetForDrawing(wedgeType, dtype, out var templates, out var templateDrawingPath))
                        throw new WedgeTypeResolutionException(
                            $"No automation template is configured for wedge type '{wedgeType}' ({dtype}). " +
                            "Automation was not started.");

                    var templatePartPath = templates.PartPath;
                    var equationTemplatePathForModelPhase = templates.EquationsPath;

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
                        OutputTiffPath = dtype == DrawingType.Overlay
                            ? Path.ChangeExtension(plan.PdfPath, ".tif")
                            : null
                    };

                    report(Progress(++doneSteps, totalSteps, $"Running model phase for {article} ({dtype})…"));

                    sessions.RunJob(
                        $"Job {job.Id} {article} {dtype}",
                        SolidWorksVisible(),
                        keepAliveAllowed: true,
                        modelPhase: app =>
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

                            return modelOrchestrator.RunAsync(jobReq, app, CancellationToken.None)
                                                    .GetAwaiter()
                                                    .GetResult();
                        },
                        drawingPhase: (app, modelResultPath) =>
                        {
                            report(Progress(++doneSteps, totalSteps, $"Running drawing phase for {article} ({dtype})…"));

                            Func<object?> runModelAutomation = () => modelResultPath;

                            var executor = dtype == DrawingType.Overlay ? "Overlay" : "Production";
                            _logger.LogInformation(
                                "Job {JobId}: {Subclass} {Type} → {Executor} executor.",
                                job.Id,
                                subclass,
                                dtype,
                                executor);

                            if (dtype == DrawingType.Overlay)
                            {
                                DrawingAutomationExecutor.RunOverlay(
                                    app,
                                    run,
                                    drawingData,
                                    runModelAutomation,
                                    plannedOverlayDimensions: null);
                            }
                            else
                            {
                                DrawingAutomationExecutor.RunProduction(
                                    app,
                                    run,
                                    drawingData,
                                    runModelAutomation);
                            }
                        });

                    var expectedOutput = dtype == DrawingType.Overlay
                        ? run.OutputTiffPath
                        : run.OutputPdfPath;

                    // Do not mark a run as successful when export silently
                    // produced no file (especially important for Overlay/TIFF).
                    if (string.IsNullOrWhiteSpace(expectedOutput) ||
                        !File.Exists(expectedOutput) ||
                        new FileInfo(expectedOutput).Length == 0)
                    {
                        throw new IOException(
                            $"{dtype} export did not produce a usable output file: '{expectedOutput}'.");
                    }

                    lastResultArtifact = expectedOutput;
                    _logger.LogInformation(
                        "Job {JobId}: completed article={Article}, type={Type}, output='{Out}'",
                        job.Id, article, dtype, expectedOutput);
                }
            }

            report(new ProgressUpdate(100, "All automation completed."));
            return lastResultArtifact ?? outputRootBase;
        }
        finally
        {
            // ALWAYS shut down the automation-owned process after the whole
            // HTTP job, whether successful, failed, or cancelled. This runs on
            // the same STA as the drawing operations. It does not kill users'
            // unrelated SolidWorks processes.
            try
            {
                sessions?.DropSession();
                _logger.LogInformation("Job {JobId}: SolidWorks session closed after API job.", job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobId}: SolidWorks shutdown had an issue.", job.Id);
            }
        }
    }

    private Dictionary<string, ValidatedWedgeData> LoadAndValidateWedges(
        JobInfo job,
        RunRequest payload,
        WedgeSubclass subclass,
        GetWedgeData getWedge,
        Action<ProgressUpdate> report)
    {
        var validated =
            new Dictionary<string, ValidatedWedgeData>(
                StringComparer.OrdinalIgnoreCase);

        var errors =
            new List<DimensionValidationIssue>();

        foreach (var articleRaw in payload.ArticleNumbers!
                     .Where(a => !string.IsNullOrWhiteSpace(a))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var article =
                articleRaw.Trim();

            report(
                new ProgressUpdate(
                    0,
                    $"Validating wedge data for {article} ({subclass})…"));

            var wedgeData =
                getWedge
                    .ExecuteAsync(
                        article,
                        subclass,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

            var wedgeType =
                ResolveWedgeType(
                    article,
                    wedgeData,
                    payload);

            var validationResult =
                WedgeDimensionValidator.Validate(
                    wedgeData,
                    wedgeType);

            if (!validationResult.IsValid)
            {
                errors.AddRange(
                    validationResult.Issues);

                _logger.LogWarning(
                    "Job {JobId}: validation failed for " +
                    "article={Article}, subclass={Subclass}, wtype={WType}, issues={IssueCount}.",
                    job.Id,
                    article,
                    subclass,
                    wedgeType,
                    validationResult.Issues.Count);

                continue;
            }

            _logger.LogInformation(
                "Job {JobId}: validation passed for " +
                "article={Article}, subclass={Subclass}, wtype={WType}.",
                job.Id,
                article,
                subclass,
                wedgeType);

            validated[article] =
                new ValidatedWedgeData(
                    wedgeData,
                    wedgeType);
        }

        if (errors.Count > 0)
        {
            var first =
                errors[0];

            var result =
                new DimensionValidationResult(
                    first.ArticleNumber,
                    first.WedgeType,
                    errors);

            throw new WedgeDimensionValidationException(
                result);
        }

        if (validated.Count == 0)
        {
            throw new InvalidOperationException(
                "Job payload does not contain any non-empty article numbers.");
        }

        report(
            new ProgressUpdate(
                0,
                "Wedge data validation passed. Starting SolidWorks automation…"));

        return validated;
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

    private static WedgeType ResolveWedgeType(
        string article,
        WedgeData wedgeData,
        RunRequest payload)
    {
        string? storedType = null;
        string? storedStyle = null;
        string? payloadType = null;

        if (TryGetPropertyIgnoreCase(wedgeData, "wedge_type", out var storedTypeValue))
        {
            storedType = storedTypeValue;

            if (TryResolveSupportedWedgeType(storedType, out var parsedFromStoredType))
                return parsedFromStoredType;
        }

        if (TryGetPropertyIgnoreCase(wedgeData, "wedge_style", out var storedStyleValue))
        {
            storedStyle = storedStyleValue;

            if (TryResolveSupportedWedgeType(storedStyle, out var parsedFromStoredStyle))
                return parsedFromStoredStyle;
        }

        if (payload.Options is not null &&
            payload.Options.TryGetValue("wedgeType", out var payloadTypeValue))
        {
            payloadType = payloadTypeValue;

            if (TryResolveSupportedWedgeType(payloadType, out var parsedFromPayload))
                return parsedFromPayload;
        }

        throw new WedgeTypeResolutionException(
            $"Wedge type could not be identified for article '{article}'. Automation was not started. " +
            $"wedge_type={DisplayWedgeTypeValue(storedType)}, " +
            $"wedge_style={DisplayWedgeTypeValue(storedStyle)}, " +
            $"options.wedgeType={DisplayWedgeTypeValue(payloadType)}. " +
            $"Supported wedge types: {SupportedWedgeTypesText}.");
    }

    private static bool TryResolveSupportedWedgeType(
        string? rawValue,
        out WedgeType wedgeType)
    {
        wedgeType = default;

        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        var token = rawValue.Trim().ToUpperInvariant();

        switch (token)
        {
            case "CKVD":
                wedgeType = WedgeType.CKVD;
                return true;

            case "COB":
                wedgeType = WedgeType.COB;
                return true;

            case "UTUS":
                wedgeType = WedgeType.UTUS;
                return true;

            case "OSG7":
                wedgeType = WedgeType.OSG7;
                return true;

            case "FP":
                wedgeType = WedgeType.FP;
                return true;

            case "4516":
                wedgeType = WedgeType._4516;
                return true;

            case "ABT":
                wedgeType = WedgeType.ABT;
                return true;

            case "AB16":
                wedgeType = WedgeType.AB16;
                return true;

            case "45CK":
                wedgeType = WedgeType._45CK;
                return true;

            case "M":
                wedgeType = WedgeType.M;
                return true;

            case "1001":
            case "1007":
            case "1300":
            case "1005A":
            case "_1001":
                wedgeType = WedgeType._1001;
                return true;
        }

        if (!WedgeStyleParser.TryParseWedgeType(rawValue, out var parsed))
            return false;

        if (!IsSupportedWedgeType(parsed))
            return false;

        wedgeType = parsed;
        return true;
    }

    private static bool IsSupportedWedgeType(WedgeType wedgeType)
    {
        return wedgeType switch
        {
            WedgeType.CKVD => true,
            WedgeType.COB => true,
            WedgeType.UTUS => true,
            WedgeType.OSG7 => true,
            WedgeType.FP => true,
            WedgeType._4516 => true,
            WedgeType.ABT => true,
            WedgeType.AB16 => true,
            WedgeType._45CK => true,
            WedgeType.M => true,
            WedgeType._1001 => true,
            _ => false
        };
    }

    private static string DisplayWedgeTypeValue(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<missing-or-empty>"
            : $"'{value.Trim()}'";

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
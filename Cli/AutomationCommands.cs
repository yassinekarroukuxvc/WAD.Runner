using Microsoft.Extensions.DependencyInjection;

using WAD.Runner.Application;
using WAD.Runner.Application.UseCases;
using WAD.Runner.DataManagement.Domain.Validation;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Execution;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.Solidworks.Adapters;

using static WAD.Runner.Cli.CliArgs;

namespace WAD.Runner.Cli;

/// <summary>SolidWorks automation commands: run-model and run-drawing (model phase + drawing phase).</summary>
internal static class AutomationCommands
{
    public static async Task RunModelAsync(IServiceProvider services, string[] args)
    {
        var (article, subclass) = ParseArticleAndSubclass(args);

        var dtypeStr = GetArgValue(args, "--dtype") ?? "Production";
        if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;

        if (!TryParseWedgeTypeEnum(args, out var wedgeTypeEnum, out var wedgeTypeError))
        {
            Logger.Error(wedgeTypeError);
            Console.WriteLine(wedgeTypeError);
            Environment.ExitCode = 2;
            return;
        }

        var modelTemplates = WedgeTemplateCatalog.GetForModel(wedgeTypeEnum);
        var partTemplatePath = modelTemplates.PartPath;
        var equationTemplatePath = modelTemplates.EquationsPath;

        var outputRoot = Path.Combine("Resources", "Out");
        Directory.CreateDirectory(outputRoot);

        Logger.Info($"[run-model] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");
        Logger.Info($"[run-model] Template(Part)='{partTemplatePath}'");
        Logger.Info($"[run-model] Template(Equations)='{equationTemplatePath}'");
        Logger.Info($"[run-model] OutputRoot='{outputRoot}'");

        var sessFactory = services.GetRequiredService<ISwSessionFactory>();
        var getWedge = services.GetRequiredService<GetWedgeData>();
        var orchestrator = services.GetRequiredService<ModelAutomationOrchestrator>();

        try
        {
            var wedgeData = await getWedge.ExecuteAsync(article, subclass, CancellationToken.None);

            WedgeDimensionValidator.ValidateOrThrow(
                wedgeData,
                wedgeTypeEnum);

            Logger.Success(
                $"[run-model] Validation passed -> Article={article}, " +
                $"Subclass={subclass}, WedgeType={wedgeTypeEnum}");

            SolidWorksProcessKiller.KillAll(killVbaServer: true);
            using var sw = sessFactory.Create(visible: SolidWorksVisible(services, args));

            var job = new ModelJobRequest
            {
                ArticleNumber = article,
                Subclass = subclass,
                DrawingType = dtype,
                OutputRoot = outputRoot,
                PartTemplatePath = partTemplatePath,
                EquationTemplatePath = equationTemplatePath,
                FileBase = null,
                WedgeData = wedgeData,
                WedgeType = wedgeTypeEnum
            };

            var resultPath = await orchestrator.RunAsync(job, sw.App, CancellationToken.None);

            Logger.Success($"[run-model] Completed. Output: {resultPath}");
            Console.WriteLine($"Model automation complete.\nOutput: {resultPath}");
        }
        catch (WedgeDimensionValidationException ex)
        {
            Logger.Error("[run-model] Dimension validation failed:");
            Logger.Error(ex.Message);
            Console.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Logger.Error("[run-model] Failed:");
            Logger.Error(ex.ToString());
            Console.WriteLine("Model automation failed:");
            Console.WriteLine(ex.ToString());
            Environment.ExitCode = 1;
        }
    }

    public static async Task RunDrawingAsync(IServiceProvider services, string[] args)
    {
        var (article, subclass) = ParseArticleAndSubclass(args);

        var dtypeStr = GetArgValue(args, "--dtype") ?? "Production";
        if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;

        if (!TryParseWedgeTypeEnum(args, out var wedgeTypeEnum, out var wedgeTypeError))
        {
            Logger.Error(wedgeTypeError);
            Console.WriteLine(wedgeTypeError);
            Environment.ExitCode = 2;
            return;
        }

        if (!WedgeTemplateCatalog.TryGetForDrawing(wedgeTypeEnum, dtype, out var templates, out var templateDrawingPath))
            throw new InvalidOperationException(
                $"No drawing automation template is configured for wedge type '{wedgeTypeEnum}' ({dtype}).");

        var templatePartPath = templates.PartPath;
        var equationTemplatePathForModelPhase = templates.EquationsPath;

        Logger.Info($"[run-drawing] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");
        Logger.Info($"[run-drawing] Template(Part)='{templatePartPath}'");
        Logger.Info($"[run-drawing] Template(Drawing)='{templateDrawingPath}'");

        var getWedge = services.GetRequiredService<GetWedgeData>();
        var getDrawing = services.GetRequiredService<GetDrawingData>();

        var wedgeData = await getWedge.ExecuteAsync(article, subclass, CancellationToken.None);

        var validationResult = WedgeDimensionValidator.Validate(wedgeData, wedgeTypeEnum);
        if (!validationResult.IsValid)
        {
            var message = validationResult.ToUserMessage();

            Logger.Error(
                $"[run-drawing] Validation failed -> Article={article}, " +
                $"Subclass={subclass}, WedgeType={wedgeTypeEnum}");

            Logger.Error(message);
            Console.WriteLine(message);
            Environment.ExitCode = 1;
            return;
        }

        Logger.Success(
            $"[run-drawing] Validation passed -> Article={article}, " +
            $"Subclass={subclass}, WedgeType={wedgeTypeEnum}");

        var drawingData = await getDrawing.ExecuteAsync(dtype, subclass, wedgeTypeEnum, article, CancellationToken.None);

        var outputRootBase = Path.Combine("Resources", "Out");
        Directory.CreateDirectory(outputRootBase);

        var plan = WAD.Runner.ModelAutomation.Common.PathPlanner.Build(
            article: article,
            subclass: subclass,
            drawingType: dtype,
            outputRoot: outputRootBase,
            fileBase: null);

        var modDrawingPath = Path.Combine(plan.WorkDir, $"{plan.FileBase}.SLDDRW");

        var run = new DrawingRun
        {
            WedgeType = wedgeTypeEnum,
            TemplatePartPath = templatePartPath,
            TemplateDrawingPath = templateDrawingPath,
            ModPartPath = plan.PartPath,
            ModDrawingPath = modDrawingPath,
            EquationsPath = plan.EquationsPath,
            Wedge = wedgeData,
            OutputPdfPath = plan.PdfPath,
            OutputTiffPath = null
        };

        // Model phase, then drawing phase. The session manager decides whether both phases share one
        // SolidWorks and watches for hangs (Runner:SolidWorks:Performance); the CLI always closes
        // SolidWorks at the end because the process exits.
        var sessions = services.GetRequiredService<SolidWorksSessionManager>();
        var orchestrator = services.GetRequiredService<ModelAutomationOrchestrator>();
        var visible = SolidWorksVisible(services, args);
        var modelFailed = false;

        try
        {
            sessions.RunJob(
                $"{article} {dtype}",
                visible,
                keepAliveAllowed: false,
                modelPhase: app =>
                {
                    var job = new ModelJobRequest
                    {
                        ArticleNumber = article,
                        Subclass = subclass,
                        DrawingType = dtype,
                        OutputRoot = outputRootBase,
                        PartTemplatePath = templatePartPath,
                        EquationTemplatePath = equationTemplatePathForModelPhase,
                        FileBase = plan.FileBase,
                        WedgeData = wedgeData,
                        WedgeType = wedgeTypeEnum
                    };

                    try
                    {
                        return orchestrator.RunAsync(job, app, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        modelFailed = true;
                        throw;
                    }
                },
                drawingPhase: (app, modelResultPath) =>
                {
                    modelFailed = false;
                    Func<object?> runModelAutomation = () => modelResultPath;

                    var executor = dtype == DrawingType.Overlay ? "Overlay" : "Production";
                    Logger.Info($"[run-drawing] Subclass={subclass}, Type={dtype} → using {executor} drawing executor…");

                    if (dtype == DrawingType.Overlay)
                        DrawingAutomationExecutor.RunOverlay(app, run, drawingData, runModelAutomation, plannedOverlayDimensions: null);
                    else
                        DrawingAutomationExecutor.RunProduction(app, run, drawingData, runModelAutomation);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(modelFailed ? "[run-drawing] Model phase failed:" : "[run-drawing] Failed:");
            Logger.Error(ex.ToString());
            Environment.ExitCode = 1;
            return;
        }
        finally
        {
            sessions.DropSession();
        }

        Logger.Success("[run-drawing] Completed.");
    }
}

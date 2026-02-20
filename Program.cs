// Program.cs  (root of WAD.Runner)
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// App logging
using WAD.Runner.Application;                     // Logger

// Ports & use-cases
using WAD.Runner.Application.Ports;
using WAD.Runner.Application.UseCases;

// Infra adapters
using WAD.Runner.DataManagement.Infrastructure.Serialization;

// Domain enums
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

// Infra adapters (DB)
using WAD.Runner.DataManagement.Infrastructure.Adapters;
using WAD.Runner.DataManagement.Infrastructure.Sqlite;

// Part Automation
using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Execution;
using WAD.Runner.PartAutomation.Jobs;

// Drawing Automation
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Executors.FG;
using WAD.Runner.DrawingAutomation.Executors.PGB;
using WAD.Runner.DrawingAutomation.Common;

// SolidWorks sessions/factory (folder: /Solidworks/Adapters)
using WAD.Runner.Solidworks.Adapters;

// Alias SolidWorks COM types to avoid IConfiguration/Environment ambiguities
using Sw = SolidWorks.Interop.sldworks;
using WAD.Runner.SolidWorks.Adapters;
using WAD.Runner.DrawingAutomation.Views;

// API host
using WAD.Runner.Api;
using WAD.Runner.PartAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.SolidWorks;
using WAD.Runner.ModelAutomation.Common;


Logger.Info("[Boot] Building host…");

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
           .AddEnvironmentVariables();
    })
    .ConfigureLogging((ctx, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .ConfigureServices((ctx, services) =>
    {
        var cs = ctx.Configuration.GetConnectionString("ProAlphaSqlite")
                 ?? throw new InvalidOperationException("Missing ConnectionStrings:ProAlphaSqlite");
        services.AddSingleton(new ProAlphaRepository(cs));

        var firma = ctx.Configuration.GetValue<int>("ProAlpha:Firma", 1);
        var language = ctx.Configuration.GetValue<string>("ProAlpha:Language", "E");

        services.AddSingleton<IWedgeDataSource>(sp =>
            new SqliteWedgeDataSource(
                sp.GetRequiredService<ProAlphaRepository>(),
                firma,
                language,
                sp.GetService<ILogger<SqliteWedgeDataSource>>()
            )
        );

        var drawingCfgPath = ctx.Configuration["DrawingConfig:Path"] ?? "Infrastructure/Config/drawing_config.json";
        services.AddSingleton<IDrawingDataSource>(_ => new JsonDrawingDataSource(drawingCfgPath));

        services.AddTransient<GetWedgeData>();
        services.AddTransient<GetDrawingData>();
        services.AddTransient<BuildAnnotationSet>();
        services.AddTransient<PlanDrawing>();

        services.AddSingleton<ISwSessionFactory, SwServiceFactory>();

        services.AddSingleton<IPartAutomationService, PartAutomationService>();
        services.AddSingleton<PartAutomationOrchestrator>();

        ////
        
        services.AddSingleton<ModelDimensionApplier>();
        services.AddSingleton<ModelAutomationOrchestrator>();

        ////
    })
    .Build();

Logger.Success("[Boot] Host ready.");

var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
jsonOpts.Converters.Add(new DimensionKeyJsonConverter());

var cmd = args.FirstOrDefault()?.ToLowerInvariant();
Logger.Info($"[CLI] Command = '{cmd ?? "(none)"}'");

switch (cmd)
{
    case "serve-api":
        {
            Logger.Info("[serve-api] Starting minimal API host…");
            await ApiHost.RunAsync(args);
            break;
        }

    case "get-wedge":
        {
            var (article, subclass) = ParseArticleAndSubclass(args);
            Logger.Info($"[get-wedge] Article={article}, Subclass={subclass}");
            var usecase = host.Services.GetRequiredService<GetWedgeData>();
            var data = await usecase.ExecuteAsync(article, subclass, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(data, jsonOpts));
            Logger.Success("[get-wedge] Done.");
            break;
        }

    case "get-drawing":
        {
            var (article, subclass) = ParseArticleAndSubclass(args);
            var dtypeStr = GetArgValue(args, "--dtype") ?? "Production";
            if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;

            var wedgeTypeEnum = ParseWedgeTypeEnum(args);

            Logger.Info($"[get-drawing] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");

            var usecase = host.Services.GetRequiredService<GetDrawingData>();
            var data = await usecase.ExecuteAsync(dtype, subclass, wedgeTypeEnum, article, CancellationToken.None);
            Console.WriteLine(JsonSerializer.Serialize(data, jsonOpts));
            Logger.Success("[get-drawing] Done.");
            break;
        }

    case "plan-lite":
        {
            var (article, subclass, dtype) = ParsePlanArgs(args);
            var wedgeTypeEnum = ParseWedgeTypeEnum(args);

            Logger.Info($"[plan-lite] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");

            var uc = host.Services.GetRequiredService<BuildAnnotationSet>();
            try
            {
                var (dims, notes, tables, _) =
                    await uc.ExecuteAsync(article, subclass, dtype, wedgeTypeEnum, CancellationToken.None);

                var payload = new
                {
                    Article = article,
                    Subclass = subclass.ToString(),
                    DrawingType = dtype.ToString(),
                    WedgeType = wedgeTypeEnum.ToString(),
                    Dimensions = dims.Select(d => new
                    {
                        d.Id,
                        d.View,
                        Key = d.Key.Value,
                        Axis = d.Axis.ToString(),
                        Pos = new { X = d.PositionMm[0], Y = d.PositionMm[1] },
                        Nominal = new { Unit = d.Nominal.Unit.ToString(), Value = d.Nominal.Value },
                        Tol = new { LTol = d.Tol.Lower.Value, UTol = d.Tol.Upper.Value },
                        Style = d.Style.ToString(),
                        Comment = d.Comment
                    }),
                    Notes = notes.Select(n => new
                    {
                        n.Id,
                        n.Text,
                        Pos = new { X = n.PositionMm[0], Y = n.PositionMm[1] }
                    }),
                    Tables = tables.Select(t => new
                    {
                        t.Id,
                        Pos = new { X = t.PositionMm[0], Y = t.PositionMm[1] },
                        Size = t.SizeMm is null ? null : new { W = t.SizeMm[0], H = t.SizeMm[1] }
                    })
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                Logger.Success("[plan-lite] Done.");
            }
            catch (Exception ex)
            {
                Logger.Error("[plan-lite] Failed:");
                Logger.Error(ex.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }

    case "list-articles":
        {
            var repo = host.Services.GetRequiredService<ProAlphaRepository>();
            var cfg = host.Services.GetRequiredService<IConfiguration>();
            var firma = cfg.GetValue<int>("ProAlpha:Firma", 1);

            var limitStr = GetArgValue(args, "--limit") ?? "20";
            _ = int.TryParse(limitStr, out var limit);
            if (limit <= 0) limit = 20;

            Logger.Info($"[list-articles] Firma={firma}, Limit={limit}");
            var rows = await repo.GetFirstArticlesAsync(firma, limit, CancellationToken.None);
            Console.WriteLine("Artikel | xPartSpec");
            Console.WriteLine("-------------------");
            foreach (var (artikel, partSpec) in rows)
                Console.WriteLine($"{artikel} | {partSpec}");
            if (rows.Count == 0) Console.WriteLine("(no rows or wrong Firma)");
            Logger.Success("[list-articles] Done.");
            break;
        }

    case "show-article":
        {
            var repo = host.Services.GetRequiredService<ProAlphaRepository>();
            var cfg = host.Services.GetRequiredService<IConfiguration>();
            var firma = cfg.GetValue<int>("ProAlpha:Firma", 1);
            var article = GetArgValue(args, "--article") ?? throw new ArgumentException("--article is required");

            Logger.Info($"[show-article] Firma={firma}, Article={article}");
            var partSpec = await repo.GetPartSpecAsync(firma, article, CancellationToken.None);
            Console.WriteLine(partSpec is null
                ? $"Article {article} not found or has no PartSpec."
                : $"Article {article} → PartSpec = {partSpec}");
            Logger.Success("[show-article] Done.");
            break;
        }

    case "db-info":
        {
            var cfg = host.Services.GetRequiredService<IConfiguration>();
            var firma = cfg.GetValue<int>("ProAlpha:Firma", 1);
            var languageEcho = cfg.GetValue<string>("ProAlpha:Language", "E");
            Logger.Info($"[db-info] Firma={firma}");
            Console.WriteLine($"Database Info (Firma {firma}, Language {languageEcho})");
            Logger.Success("[db-info] Done.");
            break;
        }

    case "run-part":
        {
            SolidWorksProcessKiller.KillAll(killVbaServer: true);
            var (article, subclass) = ParseArticleAndSubclass(args);
            var dtypeStr = GetArgValue(args, "--dtype") ?? "Production";
            if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;

            var wedgeTypeEnum = ParseWedgeTypeEnum(args);

            string partTemplatePath;
            string equationTemplatePath;

            switch (wedgeTypeEnum)
            {
                case WedgeType.COB:
                    partTemplatePath = Path.Combine(
                        "Resources", "Templates", "COB", "COB template 02-14-2026",
                        "wedge-auto-draw-COB-3d-model_sw_version_2023.SLDPRT");

                    equationTemplatePath = Path.Combine(
                        "Resources", "Templates", "COB", "COB template 02-14-2026",
                        "wedge-auto-draw-COB-3d-equation.txt");
                    break;

                case WedgeType.OSG7:
                    partTemplatePath = Path.Combine("Resources", "Templates", "OSG7", "wedge_auto_draw_OSG7_3d.SLDPRT");
                    equationTemplatePath = Path.Combine("Resources", "Templates", "OSG7", "equations_OSG7.txt");
                    break;

                case WedgeType.CKVD:
                default:
                    partTemplatePath = Path.Combine("Resources", "Templates", "CKVD", "CKVDv2","CKVD_2023.SLDPRT");
                    equationTemplatePath = Path.Combine("Resources", "Templates", "CKVD", "CKVDv2", "CK.txt");
                    break;
            }

            var outputRoot = Path.Combine("Resources", "Out");
            Directory.CreateDirectory(outputRoot);

            Logger.Info($"[run-part] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");
            Logger.Info($"[run-part] Template(Part)='{partTemplatePath}'");
            Logger.Info($"[run-part] Template(Equations)='{equationTemplatePath}'");
            Logger.Info($"[run-part] OutputRoot='{outputRoot}'");

            var orchestrator = host.Services.GetRequiredService<PartAutomationOrchestrator>();
            var sessFactory = host.Services.GetRequiredService<ISwSessionFactory>();
            var getWedge = host.Services.GetRequiredService<GetWedgeData>();

            try
            {
                using var sw = sessFactory.Create(visible: true);

                var wedgeData = await getWedge.ExecuteAsync(article, subclass, CancellationToken.None);

                var job = new PartJobRequest
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
                Logger.Success($"[run-part] Completed. Output: {resultPath}");
                Console.WriteLine($"Part automation complete.\nOutput: {resultPath}");
            }
            catch (Exception ex)
            {
                Logger.Error("[run-part] Failed:");
                Logger.Error(ex.ToString());
                Console.WriteLine("Part automation failed:");
                Console.WriteLine(ex.ToString());
                Environment.ExitCode = 1;
            }
            break;
        }

    case "run-drawing":
        {
            SolidWorksProcessKiller.KillAll(killVbaServer: true);
            var (article, subclass) = ParseArticleAndSubclass(args);
            var dtypeStr = GetArgValue(args, "--dtype") ?? "Production";
            if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;

            var wedgeTypeEnum = ParseWedgeTypeEnum(args);

            string templatePartPath;
            string templateDrawingPath;
            string equationTemplatePathForPartPhase;

            switch (wedgeTypeEnum)
            {
                case WedgeType.COB:
                    templatePartPath = Path.Combine("Resources", "Templates", "COB", "COB template 02-14-2026", "wedge-auto-draw-COB-3d-model_sw_version_2023.SLDPRT");
                    templateDrawingPath = Path.Combine("Resources", "Templates", "COB", "COB_Drawings.SLDDRW");
                    equationTemplatePathForPartPhase = Path.Combine("Resources", "Templates", "COB", "COB template 02-14-2026", "wedge-auto-draw-COB-3d-equation.txt");
                    break;

                case WedgeType.OSG7:
                    templatePartPath = Path.Combine("Resources", "Templates", "OSG7", "wedge_auto_draw_OSG7_3d.SLDPRT");
                    templateDrawingPath = dtype switch
                    {
                        DrawingType.Overlay =>
                            Path.Combine("Resources", "Templates", "OSG7", "OSG7_OVERLAY_TEMPLATE.SLDDRW"),
                        DrawingType.Production or DrawingType.Customer or _ =>
                            Path.Combine("Resources", "Templates", "OSG7", "wedge_auto_draw_OSG7_3d.SLDDRW"),
                    };
                    equationTemplatePathForPartPhase = Path.Combine("Resources", "Templates", "OSG7", "equations_OSG7.txt");
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

            Logger.Info($"[run-drawing] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");
            Logger.Info($"[run-drawing] Template(Part)='{templatePartPath}'");
            Logger.Info($"[run-drawing] Template(Drawing)='{templateDrawingPath}'");

            var getWedge = host.Services.GetRequiredService<GetWedgeData>();
            var getDrawing = host.Services.GetRequiredService<GetDrawingData>();
            var wedgeData = await getWedge.ExecuteAsync(article, subclass, CancellationToken.None);
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

            string? partResultPath = null;
            try
            {
                var orchestrator = host.Services.GetRequiredService<PartAutomationOrchestrator>();
                var sessFactory = host.Services.GetRequiredService<ISwSessionFactory>();

                using var swPart = sessFactory.Create(visible: true);

                var job = new PartJobRequest
                {
                    ArticleNumber = article,
                    Subclass = subclass,
                    DrawingType = dtype,
                    OutputRoot = outputRootBase,
                    PartTemplatePath = templatePartPath,
                    EquationTemplatePath = equationTemplatePathForPartPhase,
                    FileBase = plan.FileBase,
                    WedgeData = wedgeData,
                    WedgeType = wedgeTypeEnum
                };

                partResultPath = await orchestrator.RunAsync(job, swPart.App, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Error("[run-drawing] Part phase failed:");
                Logger.Error(ex.ToString());
                Environment.ExitCode = 1;
                break;
            }

            var sessFactory2 = host.Services.GetRequiredService<ISwSessionFactory>();
            using (var swDraw = sessFactory2.Create(visible: true))
            {
                Func<object?> runPartAutomation = () => partResultPath;

                switch (subclass)
                {
                    case WedgeSubclass.PGB:
                        switch (dtype)
                        {
                            case DrawingType.Customer:
                                Logger.Info("[run-drawing] Subclass=PGB, Type=Customer → using FG Customer executor (temporary).");
                                FgCustomerDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                break;

                            case DrawingType.Overlay:
                                Logger.Info("[run-drawing] Subclass=PGB, Type=Overlay → using PGB Overlay drawing executor…");
                                PgbOverlayDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                break;

                            case DrawingType.Production:
                            default:
                                Logger.Info("[run-drawing] Subclass=PGB, Type=Production → using PGB Production drawing executor…");
                                PgbProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                break;
                        }
                        break;

                    case WedgeSubclass.FG:
                    default:
                        switch (dtype)
                        {
                            case DrawingType.Customer:
                                Logger.Info("[run-drawing] Subclass=FG, Type=Customer → using FG Customer drawing executor…");
                                FgCustomerDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                break;

                            case DrawingType.Overlay:
                                Logger.Info("[run-drawing] Subclass=FG, Type=Overlay → using FG Overlay drawing executor…");
                                FgOverlayDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                break;

                            case DrawingType.Production:
                            default:
                                Logger.Info("[run-drawing] Subclass=FG, Type=Production → using FG Production drawing executor…");
                                FgProductionDrawingExecutor.Run(swDraw.App, run, drawingData, runPartAutomation, plannedDims: null);
                                break;
                        }
                        break;
                }
            }

            Logger.Success("[run-drawing] Completed.");
            break;
        }

    case "run-model":
        {
            SolidWorksProcessKiller.KillAll(killVbaServer: true);

            var (article, subclass) = ParseArticleAndSubclass(args);

            var dtypeStr = GetArgValue(args, "--dtype") ?? "Production";
            if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;

            var wedgeTypeEnum = ParseWedgeTypeEnum(args);

            string partTemplatePath;
            string equationTemplatePath;

            switch (wedgeTypeEnum)
            {
                case WedgeType.COB:
                    partTemplatePath = Path.Combine(
                        "Resources", "Templates", "COB", "COB template 02-14-2026",
                        "wedge-auto-draw-COB-3d-model_sw_version_2023.SLDPRT");

                    equationTemplatePath = Path.Combine(
                        "Resources", "Templates", "COB", "COB template 02-14-2026",
                        "wedge-auto-draw-COB-3d-equation.txt");
                    break;

                case WedgeType.OSG7:
                    partTemplatePath = Path.Combine("Resources", "Templates", "OSG7", "wedge_auto_draw_OSG7_3d.SLDPRT");
                    equationTemplatePath = Path.Combine("Resources", "Templates", "OSG7", "equations_OSG7.txt");
                    break;

                case WedgeType.CKVD:
                default:
                    partTemplatePath = Path.Combine("Resources", "Templates", "CKVD", "CKVDv2", "CKVD_2023.SLDPRT");
                    equationTemplatePath = Path.Combine("Resources", "Templates", "CKVD", "CKVDv2", "CK.txt");
                    break;
            }

            var outputRoot = Path.Combine("Resources", "Out");
            Directory.CreateDirectory(outputRoot);

            Logger.Info($"[run-model] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");
            Logger.Info($"[run-model] Template(Part)='{partTemplatePath}'");
            Logger.Info($"[run-model] Template(Equations)='{equationTemplatePath}'");
            Logger.Info($"[run-model] OutputRoot='{outputRoot}'");

            var sessFactory = host.Services.GetRequiredService<ISwSessionFactory>();
            var getWedge = host.Services.GetRequiredService<GetWedgeData>();
            var orchestrator = host.Services.GetRequiredService<ModelAutomationOrchestrator>();

            try
            {
                using var sw = sessFactory.Create(visible: true);

                var wedgeData = await getWedge.ExecuteAsync(article, subclass, CancellationToken.None);

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
            catch (Exception ex)
            {
                Logger.Error("[run-model] Failed:");
                Logger.Error(ex.ToString());
                Console.WriteLine("Model automation failed:");
                Console.WriteLine(ex.ToString());
                Environment.ExitCode = 1;
            }

            break;
        }

    default:
        PrintHelp();
        break;
}

static void PrintHelp()
{
    Console.WriteLine("""
WAD.Runner CLI

Data:
  get-wedge      --article <num> --subclass <FG|PGB>
  get-drawing    --article <num> --subclass <FG|PGB> --dtype <Production|Customer|Overlay> [--wtype CKVD|COB|OSG7]
  plan-lite      --article <num> --subclass <FG|PGB> [--dtype Production|Customer|Overlay] [--wtype CKVD|COB|OSG7]

Diagnostics:
  db-info        [--limit 20]
  list-articles  [--limit 20]
  show-article   --article <num>

Part Automation:
  run-part       --article <num> --subclass <FG|PGB> [--dtype Production|Customer|Overlay] [--wtype CKVD|COB|OSG7]
                 Uses (by wtype):
                   CKVD:
                     Part template  : Resources/Templates/CKVD/CKVD_2023.SLDPRT
                     Equations file : Resources/Templates/CKVD/CK.txt
                   COB:
                     Part template  : Resources/Templates/COB/COB_Template.SLDPRT
                     Equations file : Resources/Templates/COB/equations.txt
                   OSG7:
                     Part template  : Resources/Templates/OSG7/wedge_auto_draw_OSG7_3d.SLDPRT
                     Equations file : Resources/Templates/OSG7/equations_OSG7.txt
                 Output root    : Resources/Out

Drawing Automation:
  run-drawing    --article <num> --subclass <FG|PGB> [--dtype Production|Customer|Overlay] [--wtype CKVD|COB|OSG7]
                 Templates (by wtype):
                   CKVD:
                     Part          : Resources/Templates/CKVD/CKVD_2023.SLDPRT
                     Drawing (Prod): Resources/Templates/CKVD/CKVD_2023.SLDDRW
                     Drawing (Cust): Resources/Templates/CKVD/CKVD_2023.SLDDRW
                     Drawing (Ovrl): Resources/Templates/CKVD/OVERLAY_TEMPLATE.SLDDRW
                   COB:
                     Part          : Resources/Templates/COB/COB_Template.SLDPRT
                     Drawing (all) : Resources/Templates/COB/COB_Drawings.SLDDRW
                   OSG7:
                     Part          : Resources/Templates/OSG7/wedge_auto_draw_OSG7_3d.SLDPRT
                     Drawing (Prod): Resources/Templates/OSG7/OSG7_Production.SLDDRW
                     Drawing (Ovrl): Resources/Templates/OSG7/OSG7_OVERLAY_TEMPLATE.SLDDRW
                     Equations     : Resources/Templates/OSG7/equations_OSG7.txt

API:
  serve-api      Starts the minimal API host

Examples:
  dotnet run -- run-drawing --article 3118724 --subclass FG --dtype Production --wtype OSG7
""");
}

static string? GetArgValue(string[] a, string key)
    => a.SkipWhile(x => !string.Equals(x, key, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

static (string article, WedgeSubclass subclass) ParseArticleAndSubclass(string[] a)
{
    var article = GetArgValue(a, "--article") ?? throw new ArgumentException("--article is required");
    var subclassStr = GetArgValue(a, "--subclass") ?? "FG";
    var ok = Enum.TryParse<WedgeSubclass>(subclassStr, true, out var subclass);
    if (!ok) subclass = WedgeSubclass.FG;
    return (article.Trim(), subclass);
}

static (string article, WedgeSubclass subclass, DrawingType dtype) ParsePlanArgs(string[] a)
{
    var (article, subclass) = ParseArticleAndSubclass(a);
    var dtypeStr = GetArgValue(a, "--dtype") ?? "Production";
    if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;
    return (article, subclass, dtype);
}

static WedgeType ParseWedgeTypeEnum(string[] a)
{
    var wtype = (GetArgValue(a, "--wtype") ?? "CKVD").Trim().ToUpperInvariant();

    return wtype switch
    {
        "CKVD" => WedgeType.CKVD,
        "COB" => WedgeType.COB,
        "OSG7" => WedgeType.OSG7,
        _ => WedgeType.CKVD
    };
}

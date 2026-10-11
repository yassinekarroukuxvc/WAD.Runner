using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using WAD.Runner.Api;
using WAD.Runner.Application;
using WAD.Runner.Application.Ports;
using WAD.Runner.Application.UseCases;
using WAD.Runner.DataManagement.Infrastructure.Adapters;
using WAD.Runner.DataManagement.Infrastructure.Sqlite;
using WAD.Runner.DataManagement.Infrastructure.Transport;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.SolidWorks;
using WAD.Runner.Solidworks.Adapters;

namespace WAD.Runner.Cli;

/// <summary>Builds the generic host and registers services for the CLI commands.</summary>
internal static class RunnerHost
{
    public static IHost Build(string[] args)
    {
        Logger.Info("[Boot] Building host…");

        return Host.CreateDefaultBuilder(args)
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
                // ============================================================
                // 1) WEDGE DATA SOURCE SWITCH: Java API vs SQLite
                // ============================================================
                var useJava = ctx.Configuration.GetValue<bool>("Runner:UseJavaDbApi", false);

                if (useJava)
                {
                    var baseUrl = ctx.Configuration.GetValue<string>("Runner:JavaDbApi:BaseUrl")
                                  ?? throw new InvalidOperationException("Missing Runner:JavaDbApi:BaseUrl");

                    var timeoutSec = ctx.Configuration.GetValue<int?>("Runner:JavaDbApi:TimeoutSeconds") ?? 45;
                    var apiKey = ctx.Configuration.GetValue<string>("Runner:JavaDbApi:ApiKey");

                    var firma = ctx.Configuration.GetValue<int?>("ProAlpha:Firma") ?? 200;
                    var language = ctx.Configuration.GetValue<string>("ProAlpha:Language", "E") ?? "E";

                    services.AddSingleton<JavaDatabaseSelectionContext>();
                    services.AddTransient<JavaDatabaseSelectionHandler>();

                    services.AddHttpClient("JavaLegacyWedgeTransport", http =>
                    {
                        http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
                        http.Timeout = TimeSpan.FromSeconds(timeoutSec);

                        if (!http.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
                            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

                        if (!string.IsNullOrWhiteSpace(apiKey))
                            http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
                    })
                    .AddHttpMessageHandler<JavaDatabaseSelectionHandler>();

                    services.AddSingleton<IJavaWedgeTransport>(sp =>
                    {
                        var factory = sp.GetRequiredService<IHttpClientFactory>();
                        var http = factory.CreateClient("JavaLegacyWedgeTransport");
                        return new JavaLegacyWedgeTransport(http, firma, language);
                    });

                    services.AddSingleton<IWedgeDataSource, JavaWedgeDataSource>();
                }
                else
                {
                    Logger.Info("[Boot] IWedgeDataSource = SQLite");

                    var rawCs = ctx.Configuration.GetConnectionString("ProAlphaSqlite")
                   ?? throw new InvalidOperationException("Missing ConnectionStrings:ProAlphaSqlite");

                    var cs = ResolveSqliteConnectionString(rawCs, ctx.HostingEnvironment.ContentRootPath);

                    Logger.Info($"[Boot] SQLite DB path = '{new SqliteConnectionStringBuilder(cs).DataSource}'");

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
                }

                // ============================================================
                // 2) DRAWING DATA SOURCE
                // ============================================================
                var drawingCfgPath = ctx.Configuration["DrawingConfig:Path"] ?? "Resources/Config/drawing_config.json";
                services.AddSingleton<IDrawingDataSource>(_ => new JsonDrawingDataSource(drawingCfgPath));

                // ============================================================
                // 3) USE CASES
                // ============================================================
                services.AddTransient<GetWedgeData>();
                services.AddTransient<GetDrawingData>();
                services.AddTransient<BuildAnnotationSet>();
                services.AddTransient<PlanDrawing>();

                // ============================================================
                // 4) SOLIDWORKS SESSION FACTORY
                // ============================================================
                services.AddSingleton<ISwSessionFactory, SwServiceFactory>();
                services.AddSingleton<SolidWorksSessionManager>(sp => new SolidWorksSessionManager(
                    sp.GetRequiredService<ISwSessionFactory>(), sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>()));

                // ============================================================
                // 5) MODEL AUTOMATION (new pipeline)
                // ============================================================
                services.AddSingleton<ModelDimensionApplier>();
                services.AddSingleton<ModelAutomationOrchestrator>();
            })
            .Build();
    }

    private static string ResolveSqliteConnectionString(string rawValue, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            throw new InvalidOperationException("ConnectionStrings:ProAlphaSqlite is empty.");

        SqliteConnectionStringBuilder builder;

        // Supports both:
        // 1) "Resources/Database/wedge_data.db"
        // 2) "Data Source=Resources/Database/wedge_data.db"
        if (rawValue.Contains('='))
        {
            builder = new SqliteConnectionStringBuilder(rawValue);
        }
        else
        {
            builder = new SqliteConnectionStringBuilder
            {
                DataSource = rawValue
            };
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("ConnectionStrings:ProAlphaSqlite has no database path.");

        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(
                Path.Combine(contentRootPath, builder.DataSource));
        }

        if (!File.Exists(builder.DataSource))
        {
            throw new FileNotFoundException(
                $"SQLite database file was not found: {builder.DataSource}",
                builder.DataSource);
        }

        return builder.ToString();
    }
}

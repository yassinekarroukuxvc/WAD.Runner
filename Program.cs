// Program.cs  (root of WAD.Runner)
// Entry point: builds the host, applies the --database selection and dispatches the CLI command.
// Command implementations live in Cli/.

using Microsoft.Extensions.DependencyInjection;

using WAD.Runner.Api;
using WAD.Runner.Application;
using WAD.Runner.Cli;

using static WAD.Runner.Cli.CliArgs;

var host = RunnerHost.Build(args);

Logger.Success("[Boot] Host ready.");

var cmd = args.FirstOrDefault()?.ToLowerInvariant();

// Default behavior: dotnet run => serve-api
if (string.IsNullOrWhiteSpace(cmd))
    cmd = "serve-api";

Logger.Info($"[CLI] Command = '{cmd}'");

IDisposable? cliDatabaseScope = null;
var cliDatabaseSelection = host.Services.GetService<JavaDatabaseSelectionContext>();
if (cliDatabaseSelection is not null)
{
    var rawDatabase = GetArgValue(args, "--database") ?? "Production";

    if (!JavaDatabaseTargetParser.TryParse(rawDatabase, out var cliDatabaseTarget))
    {
        var message =
            $"[CLI] ERROR: Unsupported database '{rawDatabase}'. " +
            "Supported values: Production, Test.";
        Logger.Error(message);
        Console.WriteLine(message);
        Environment.ExitCode = 2;
        return;
    }

    cliDatabaseScope = cliDatabaseSelection.Push(cliDatabaseTarget);
    Logger.Info($"[CLI] Java database = {cliDatabaseTarget}");
}

using var _cliDatabaseScope = cliDatabaseScope;

var services = host.Services;

switch (cmd)
{
    case "serve-api":
        Logger.Info("[serve-api] Starting minimal API host…");
        await ApiHost.RunAsync(args);
        break;

    case "get-wedge":     await DataCommands.GetWedgeAsync(services, args); break;
    case "get-drawing":   await DataCommands.GetDrawingAsync(services, args); break;
    case "plan-lite":     await DataCommands.PlanLiteAsync(services, args); break;

    case "list-articles": await SqliteDiagnosticsCommands.ListArticlesAsync(services, args); break;
    case "show-article":  await SqliteDiagnosticsCommands.ShowArticleAsync(services, args); break;
    case "db-info":       SqliteDiagnosticsCommands.DbInfo(services, args); break;

    case "run-drawing":   await AutomationCommands.RunDrawingAsync(services, args); break;
    case "run-model":     await AutomationCommands.RunModelAsync(services, args); break;

    default:
        PrintHelp();
        break;
}

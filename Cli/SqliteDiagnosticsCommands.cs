using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Infrastructure.Sqlite;

using static WAD.Runner.Cli.CliArgs;

namespace WAD.Runner.Cli;

/// <summary>SQLite-only diagnostics: list-articles, show-article, db-info.</summary>
internal static class SqliteDiagnosticsCommands
{
    public static async Task ListArticlesAsync(IServiceProvider services, string[] args)
    {
        var repo = services.GetService<ProAlphaRepository>();
        if (repo is null)
        {
            Logger.Warn("[list-articles] ProAlphaRepository not registered (likely UseJavaDbApi=true).");
            Console.WriteLine("SQLite repository not available when Runner:UseJavaDbApi=true.");
            Environment.ExitCode = 2;
            return;
        }

        var cfg = services.GetRequiredService<IConfiguration>();
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
    }

    public static async Task ShowArticleAsync(IServiceProvider services, string[] args)
    {
        var repo = services.GetService<ProAlphaRepository>();
        if (repo is null)
        {
            Logger.Warn("[show-article] ProAlphaRepository not registered (likely UseJavaDbApi=true).");
            Console.WriteLine("SQLite repository not available when Runner:UseJavaDbApi=true.");
            Environment.ExitCode = 2;
            return;
        }

        var cfg = services.GetRequiredService<IConfiguration>();
        var firma = cfg.GetValue<int>("ProAlpha:Firma", 1);
        var article = GetArgValue(args, "--article") ?? throw new ArgumentException("--article is required");

        Logger.Info($"[show-article] Firma={firma}, Article={article}");
        var partSpec = await repo.GetPartSpecAsync(firma, article, CancellationToken.None);
        Console.WriteLine(partSpec is null
            ? $"Article {article} not found or has no PartSpec."
            : $"Article {article} → PartSpec = {partSpec}");
        Logger.Success("[show-article] Done.");
    }

    public static void DbInfo(IServiceProvider services, string[] args)
    {
        var cfg = services.GetRequiredService<IConfiguration>();
        var firma = cfg.GetValue<int>("ProAlpha:Firma", 1);
        var languageEcho = cfg.GetValue<string>("ProAlpha:Language", "E");
        Logger.Info($"[db-info] Firma={firma}");
        Console.WriteLine($"Database Info (Firma {firma}, Language {languageEcho})");
        Logger.Success("[db-info] Done.");
    }
}

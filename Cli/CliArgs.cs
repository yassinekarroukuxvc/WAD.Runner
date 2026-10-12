using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.Cli;

/// <summary>Command-line argument parsing and help text.</summary>
internal static class CliArgs
{
    public static void PrintHelp()
    {
        Console.WriteLine("""
    WAD.Runner CLI

    Default:
      dotnet run     Starts the minimal API host

    Data:
      get-wedge      --article <num> --subclass <FG|PGB> [--database Production|Test]
      get-drawing    --article <num> --subclass <FG|PGB> [--database Production|Test] --dtype <Production|Customer|Overlay> --wtype <CKVD|COB|UTUS|OSG7|FP|4516|ABT|AB16|45CK|M|1001|1007|1300|1005A>
      plan-lite      --article <num> --subclass <FG|PGB> [--database Production|Test] [--dtype Production|Customer|Overlay] --wtype <CKVD|COB|UTUS|OSG7|FP|4516|ABT|AB16|45CK|M|1001|1007|1300|1005A>

    Diagnostics (SQLite only):
      db-info        [--limit 20]
      list-articles  [--limit 20]
      show-article   --article <num>

    Drawing Automation:
      run-drawing    --article <num> --subclass <FG|PGB> [--database Production|Test] [--dtype Production|Customer|Overlay] [--hidden|--visible] --wtype <CKVD|COB|UTUS|OSG7|FP|4516|ABT|AB16|45CK|M|1001|1007|1300|1005A>

    Model Automation:
      run-model      --article <num> --subclass <FG|PGB> [--database Production|Test] [--dtype Production|Customer|Overlay] [--hidden|--visible] --wtype <CKVD|COB|UTUS|OSG7|FP|4516|ABT|AB16|45CK|M|1001|1007|1300|1005A>

    API:
      serve-api      Starts the minimal API host

    Examples:
      dotnet run
      dotnet run -- get-wedge --article 3118724 --subclass FG
      dotnet run -- run-drawing --article 3118724 --subclass FG --dtype Production --wtype OSG7
    """);
    }

    /// <summary>
    /// SolidWorks is hidden by default.
    /// --visible explicitly enables the window.
    /// --hidden explicitly disables the window.
    /// </summary>
    public static bool SolidWorksVisible(
        IServiceProvider services,
        string[] args)
    {
        if (args.Any(x =>
            string.Equals(x, "--hidden", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (args.Any(x =>
            string.Equals(x, "--visible", StringComparison.OrdinalIgnoreCase)))
            return true;

        var cfg = services.GetService(
            typeof(Microsoft.Extensions.Configuration.IConfiguration))
            as Microsoft.Extensions.Configuration.IConfiguration;

        return cfg?["Runner:SolidWorks:Visible"] is { } raw
            && bool.TryParse(raw, out var visible)
            && visible;
    }
    public static string? GetArgValue(string[] a, string key)
        => a.SkipWhile(x => !string.Equals(x, key, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();

    public static (string article, WedgeSubclass subclass) ParseArticleAndSubclass(string[] a)
    {
        var article = GetArgValue(a, "--article") ?? throw new ArgumentException("--article is required");
        var subclassStr = GetArgValue(a, "--subclass") ?? "FG";
        var ok = Enum.TryParse<WedgeSubclass>(subclassStr, true, out var subclass);
        if (!ok) subclass = WedgeSubclass.FG;
        return (article.Trim(), subclass);
    }

    public static (string article, WedgeSubclass subclass, DrawingType dtype) ParsePlanArgs(string[] a)
    {
        var (article, subclass) = ParseArticleAndSubclass(a);
        var dtypeStr = GetArgValue(a, "--dtype") ?? "Production";
        if (!Enum.TryParse<DrawingType>(dtypeStr, true, out var dtype)) dtype = DrawingType.Production;
        return (article, subclass, dtype);
    }

    public static bool TryParseWedgeTypeEnum(string[] a, out WedgeType wedgeType, out string error)
    {
        wedgeType = default;
        error = string.Empty;

        var rawWedgeType = GetArgValue(a, "--wtype");

        if (string.IsNullOrWhiteSpace(rawWedgeType))
        {
            error =
                "[CLI] ERROR: --wtype is required. Automation was not started. " +
                "Supported wedge types: CKVD, COB, UTUS, OSG7, FP, 4516, ABT, AB16, 45CK, M, 1001, 1007, 1300, 1005A.";
            return false;
        }

        var wtype = rawWedgeType.Trim().ToUpperInvariant();

        switch (wtype)
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
                wedgeType = WedgeType._1001;
                return true;

            case "VM":
                wedgeType = WedgeType.VM;
                return true;

            default:
                error =
                    $"[CLI] ERROR: Unsupported wedge type '{rawWedgeType}'. Automation was not started. " +
                    "Supported wedge types: CKVD, COB, UTUS, OSG7, FP, 4516, ABT, AB16, 45CK, M, 1001, 1007, 1300, 1005A.";
                return false;
        }
    }
}

using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using WAD.Runner.Application;
using WAD.Runner.Application.UseCases;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Infrastructure.Serialization;

using static WAD.Runner.Cli.CliArgs;

namespace WAD.Runner.Cli;

/// <summary>Read-only data commands: get-wedge, get-drawing, plan-lite.</summary>
internal static class DataCommands
{
    private static readonly JsonSerializerOptions jsonOpts = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new DimensionKeyJsonConverter());
        return options;
    }

    public static async Task GetWedgeAsync(IServiceProvider services, string[] args)
    {
        var (article, subclass) = ParseArticleAndSubclass(args);
        Logger.Info($"[get-wedge] Article={article}, Subclass={subclass}");
        var usecase = services.GetRequiredService<GetWedgeData>();
        var data = await usecase.ExecuteAsync(article, subclass, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(data, jsonOpts));
        Logger.Success("[get-wedge] Done.");
    }

    public static async Task GetDrawingAsync(IServiceProvider services, string[] args)
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

        Logger.Info($"[get-drawing] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");

        var usecase = services.GetRequiredService<GetDrawingData>();
        var data = await usecase.ExecuteAsync(dtype, subclass, wedgeTypeEnum, article, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(data, jsonOpts));
        Logger.Success("[get-drawing] Done.");
    }

    public static async Task PlanLiteAsync(IServiceProvider services, string[] args)
    {
        var (article, subclass, dtype) = ParsePlanArgs(args);
        if (!TryParseWedgeTypeEnum(args, out var wedgeTypeEnum, out var wedgeTypeError))
        {
            Logger.Error(wedgeTypeError);
            Console.WriteLine(wedgeTypeError);
            Environment.ExitCode = 2;
            return;
        }

        Logger.Info($"[plan-lite] Article={article}, Subclass={subclass}, Type={dtype}, WedgeType={wedgeTypeEnum}");

        var uc = services.GetRequiredService<BuildAnnotationSet>();
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
    }
}

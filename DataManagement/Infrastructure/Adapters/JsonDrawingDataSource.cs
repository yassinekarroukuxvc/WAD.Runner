using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WAD.Runner.Application.Ports;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Infrastructure.Adapters;

public sealed class JsonDrawingDataSource : IDrawingDataSource
{
    private readonly string _path;
    private readonly ILogger<JsonDrawingDataSource> _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonDrawingDataSource(string path, ILogger<JsonDrawingDataSource>? log = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _log = log ?? NullLogger<JsonDrawingDataSource>.Instance;
    }

    public async Task<DrawingData> LoadAsync(DrawingType drawingType, WedgeSubclass subclass, string articleNumber, CancellationToken ct)
    {
        if (!File.Exists(_path))
            throw new FileNotFoundException($"Drawing config not found: {_path}");

        await using var fs = File.OpenRead(_path);
        var root = await JsonSerializer.DeserializeAsync<ConfigRoot>(fs, _json, ct)
                   ?? throw new InvalidOperationException("Invalid drawing_config.json (null root).");

        var bag = new Bag();

        // 1) Defaults
        Merge(bag, root.Defaults);

        // 2) DrawingType section
        if (!root.Sections.TryGetValue(drawingType.ToString(), out var section))
            throw new InvalidOperationException($"Missing section for DrawingType='{drawingType}'.");
        Merge(bag, section.Common);

        // 3) Subclass
        var sub = subclass == WedgeSubclass.FG ? section.FG : section.PGB;
        Merge(bag, sub.Common);

        // 4) Optional WedgeType override based on earlier-provided metadata
        if (bag.Metadata.TryGetValue("WedgeType", out var wedgeType) &&
            !string.IsNullOrWhiteSpace(wedgeType) &&
            section.WedgeTypeOverrides.TryGetValue(wedgeType!, out var wt))
        {
            Merge(bag, wt.Common);
        }

        return new DrawingData(
            drawingType,
            subclass,
            articleNumber,
            views: bag.Views,
            tables: bag.Tables,
            metadata: bag.Metadata
        );
    }

    // ------------------ merge helpers ------------------

    private static void Merge(Bag dst, CommonBlock? src)
    {
        if (src is null) return;

        if (src.Views is not null)
        {
            foreach (var (name, cfg) in src.Views)
            {
                if (!dst.Views.TryGetValue(name, out var view))
                {
                    view = new ViewConfig();
                    dst.Views[name] = view;
                }

                // Assign directly (no 'with')
                if (cfg.PositionMm is not null) view.PositionMm = cfg.PositionMm;
                if (cfg.Scale is not null) view.Scale = cfg.Scale.Value;

                PutAll(view.Params, cfg.Params);
                PutAll(view.Flags, cfg.Flags);
                PutAll(view.Metadata, cfg.Metadata);
            }
        }

        if (src.Tables is not null)
        {
            foreach (var (name, cfg) in src.Tables)
            {
                if (!dst.Tables.TryGetValue(name, out var tbl))
                {
                    tbl = new TableConfig();
                    dst.Tables[name] = tbl;
                }

                if (cfg.PositionMm is not null) tbl.PositionMm = cfg.PositionMm;
                if (cfg.SizeMm is not null) tbl.SizeMm = cfg.SizeMm;

                PutAll(tbl.Params, cfg.Params);
                PutAll(tbl.Flags, cfg.Flags);
                PutAll(tbl.Metadata, cfg.Metadata);
            }
        }

        PutAll(dst.Metadata, src.Metadata);
    }

    private static void PutAll<T>(IDictionary<string, T> dst, IDictionary<string, T>? src)
    {
        if (src is null) return;
        foreach (var (k, v) in src) dst[k] = v;
    }

    // ------------------ config DTOs ------------------

    private sealed class ConfigRoot
    {
        public CommonBlock? Defaults { get; init; }
        public Dictionary<string, DrawingTypeSection> Sections { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class DrawingTypeSection
    {
        public CommonBlock? Common { get; init; }
        public SubclassBlock FG { get; init; } = new();
        public SubclassBlock PGB { get; init; } = new();
        public Dictionary<string, OverrideBlock> WedgeTypeOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SubclassBlock
    {
        public CommonBlock? Common { get; init; }
    }

    private sealed class OverrideBlock
    {
        public CommonBlock? Common { get; init; }
    }

    private sealed class CommonBlock
    {
        public Dictionary<string, ViewItem>? Views { get; init; }
        public Dictionary<string, TableItem>? Tables { get; init; }
        public Dictionary<string, string?>? Metadata { get; init; }
    }

    private sealed class ViewItem
    {
        public double[]? PositionMm { get; init; }
        public double? Scale { get; init; }
        public Dictionary<string, double>? Params { get; init; }
        public Dictionary<string, bool>? Flags { get; init; }
        public Dictionary<string, string?>? Metadata { get; init; }
    }

    private sealed class TableItem
    {
        public double[]? PositionMm { get; init; }
        public double[]? SizeMm { get; init; }
        public Dictionary<string, double>? Params { get; init; }
        public Dictionary<string, bool>? Flags { get; init; }
        public Dictionary<string, string?>? Metadata { get; init; }
    }

    private sealed class Bag
    {
        public Dictionary<string, ViewConfig> Views { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TableConfig> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

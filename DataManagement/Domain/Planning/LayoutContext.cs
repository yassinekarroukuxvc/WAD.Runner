using System.Collections.Generic;
using System.Text.RegularExpressions;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning;

public sealed class LayoutContext
{
    private readonly IReadOnlyDictionary<string, double> _runtimeViewScales;

    public WedgeData Wedge { get; }
    public DrawingData Drawing { get; }

    public LayoutContext(
        WedgeData wedge,
        DrawingData drawing,
        IReadOnlyDictionary<string, double>? runtimeViewScales = null)
    {
        Wedge = wedge ?? throw new ArgumentNullException(nameof(wedge));
        Drawing = drawing ?? throw new ArgumentNullException(nameof(drawing));

        _runtimeViewScales =
            runtimeViewScales
            ?? new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetView(string name, out (double[] pos, double scale) v)
    {
        if (Drawing.Views.TryGetValue(name, out var cfg))
        {
            v = (cfg.PositionMm, GetViewScale(name));
            return true;
        }

        v = default;
        return false;
    }

    public double GetViewScale(string name, double fallback = 1.0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        if (_runtimeViewScales.TryGetValue(name, out var runtimeScale)
            && IsValidScale(runtimeScale))
        {
            return runtimeScale;
        }

        if (Drawing.Views.TryGetValue(name, out var cfg)
            && cfg is not null
            && IsValidScale(cfg.Scale))
        {
            return cfg.Scale;
        }

        return fallback;
    }

    private static bool IsValidScale(double scale)
        => double.IsFinite(scale) && scale > 0.0;

    public bool TryGetDim(string key, out Dimension d)
    {
        var k = new DimensionKey(key);
        if (Wedge.Dimensions.TryGetValue(k, out var entry))
        { d = entry; return true; }
        d = default!;
        return false;
    }

    public static bool IsRefLike(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return false;

        return Regex.IsMatch(comment, @"\b(REF|REFERENCE|MIN)\b", RegexOptions.IgnoreCase);
    }
}

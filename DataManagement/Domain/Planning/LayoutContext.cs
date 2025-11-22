using System.Text.RegularExpressions;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning;

/// <summary>
/// Read-only accessors binding a WedgeData + DrawingData for planning.
/// </summary>
public sealed class LayoutContext
{
    public WedgeData Wedge { get; }
    public DrawingData Drawing { get; }

    public LayoutContext(WedgeData wedge, DrawingData drawing)
    {
        Wedge = wedge ?? throw new ArgumentNullException(nameof(wedge));
        Drawing = drawing ?? throw new ArgumentNullException(nameof(drawing));
    }

    // ------- view helpers -------
    public bool TryGetView(string name, out (double[] pos, double scale) v)
    {
        if (Drawing.Views.TryGetValue(name, out var cfg))
        { v = (cfg.PositionMm, cfg.Scale); return true; }
        v = default; return false;
    }

    // ------- dimension helpers -------
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
        // Treat MIN like REF as requested
        return Regex.IsMatch(comment, @"\b(REF|REFERENCE|MIN)\b", RegexOptions.IgnoreCase);
    }
}

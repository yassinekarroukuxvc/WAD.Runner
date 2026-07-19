using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning;

internal static class LayoutMath
{

    internal static double[] View(LayoutContext ctx, string view)
        => ctx.Drawing.Views.TryGetValue(view, out var v) ? v.PositionMm : new double[] { 0.0, 0.0 };

    internal static double Scale(LayoutContext ctx, string view)
        => ctx.GetViewScale(view);

    internal static double Dmm(LayoutContext ctx, string key)
        => ctx.TryGetDim(key, out var d) && d.Nominal.Unit == UnitKind.Millimeter ? (double)d.Nominal.Value : 0.0;

    internal static double Ddeg(LayoutContext ctx, string key)
        => ctx.TryGetDim(key, out var d) && d.Nominal.Unit == UnitKind.Degree ? (double)d.Nominal.Value : 0.0;

    internal static double TryDdeg(LayoutContext ctx, string key)
        => ctx.TryGetDim(key, out var d) && d.Nominal.Unit == UnitKind.Degree ? (double)d.Nominal.Value : double.NaN;

    internal static double Kmm(WedgeData wedge, double TL)
    {
        if (wedge?.KValue is not null)
        {
            var k = (double)wedge.KValue.ValueMm.Value;
            if (double.IsFinite(k) && k > 0.0) return Math.Min(k, TL * 0.5);
        }

        if (wedge!.Dimensions.TryGetValue(new DimensionKey("K"), out var kd)
            && kd.Nominal.Unit == UnitKind.Millimeter)
        {
            var k2 = (double)kd.Nominal.Value;
            if (double.IsFinite(k2) && k2 > 0.0) return Math.Min(k2, TL * 0.5);
        }

        return Math.Min(TL * 0.40, TL * 0.50);
    }

    internal static double WedgeLength(LayoutContext ctx, double TL, double scale)
    {
        const double GAP = 2.0;
        const double UpperPct = 0.05;
        const double OffsetPct = 0.02;

        double k = Kmm(ctx.Wedge, TL);
        double rest = Math.Max(0.0, TL - k);

        return (UpperPct * TL + OffsetPct * TL + rest) * scale + GAP;
    }
}

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.Common;

public static class WedgeDimensionReader
{
    public static bool HasPositiveNominal(WedgeData wedge, string key, decimal eps = 0.000001m)
        => TryGetNominal(wedge, key, out var value) && value > eps;

    public static bool TryGetNominal(WedgeData wedge, string key, out decimal value)
    {
        value = 0m;
        if (wedge?.Dimensions is null) return false;
        if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null) return false;
        value = dim.Nominal.Value;
        return true;
    }

    public static bool TryGetLengthNominalMm(WedgeData wedge, string key, out decimal mm, string? logPrefix = null)
    {
        mm = 0m;
        var dim = wedge?.TryGet(DimensionKey.From(key));
        if (dim is null || dim.Nominal.Unit != UnitKind.Millimeter)
        {
            if (!string.IsNullOrWhiteSpace(logPrefix)) Logger.Warn($"[{logPrefix}] Missing/non-mm nominal: {key}");
            return false;
        }
        mm = dim.Nominal.AsMm();
        return true;
    }

    public static bool TryGetLengthToleranceMm(WedgeData wedge, string key, out decimal lowerMm, out decimal upperMm, string? logPrefix = null)
    {
        lowerMm = 0m;
        upperMm = 0m;
        var dim = wedge?.TryGet(DimensionKey.From(key));
        if (dim is null || dim.Nominal.Unit != UnitKind.Millimeter)
        {
            if (!string.IsNullOrWhiteSpace(logPrefix)) Logger.Warn($"[{logPrefix}] Missing/non-mm tolerance: {key}");
            return false;
        }
        lowerMm = dim.Tol.Lower.AsMm();
        upperMm = dim.Tol.Upper.AsMm();
        return true;
    }
}

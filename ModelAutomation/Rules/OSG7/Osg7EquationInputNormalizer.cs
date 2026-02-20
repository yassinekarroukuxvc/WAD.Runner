using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.OSG7;

public sealed class Osg7EquationInputNormalizer : IEquationInputNormalizer
{
    private const double Eps = 1e-9;

    public IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType)
    {
        // Clone into a mutable map (do NOT mutate wedge.Dimensions)
        var result = new Dictionary<DimensionKey, Dimension>(wedge.Dimensions);

        // Macro-like rules you already used:
        // If FRX missing or 0 → FRX = FR
        CopyIfMissingOrZero(result, targetKey: "FRX", sourceKey: "FR");

        // If BRX missing or 0 → BRX = BR
        CopyIfMissingOrZero(result, targetKey: "BRX", sourceKey: "BR");

        // Add more OSG7 dimension rules here as needed.

        return result;
    }

    private static void CopyIfMissingOrZero(
        IDictionary<DimensionKey, Dimension> dims,
        string targetKey,
        string sourceKey)
    {
        var tKey = new DimensionKey(targetKey);
        var sKey = new DimensionKey(sourceKey);

        bool targetMissing = !dims.TryGetValue(tKey, out var tDim) || tDim is null;
        bool targetZero = !targetMissing && IsZeroNominal(tDim!);

        if (!(targetMissing || targetZero))
            return;

        if (!dims.TryGetValue(sKey, out var sDim) || sDim is null)
            return;

        dims[tKey] = sDim;
    }

    private static bool IsZeroNominal(Dimension dim)
    {
        var v = (double)dim.Nominal.Value;
        return Math.Abs(v) <= Eps;
    }
}

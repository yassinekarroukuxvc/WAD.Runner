// PartAutomation/Rules/Equations/Osg7EquationInputNormalizer.cs
using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.Interfaces;

namespace WAD.Runner.PartAutomation.Rules.Equations;

public sealed class Osg7EquationInputNormalizer : IEquationInputNormalizer
{
    private const double Eps = 1e-9;

    public IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType)
    {
        // Clone into a mutable map (do NOT mutate wedge.Dimensions)
        var result = new Dictionary<DimensionKey, Dimension>(wedge.Dimensions);

        // Example macro rule:
        // If FRX missing or 0 → FRX = FR
        CopyIfMissingOrZero(result, targetKey: "FRX", sourceKey: "FR");

        // Example: BRX missing or 0 → BRX = BR
        CopyIfMissingOrZero(result, targetKey: "BRX", sourceKey: "BR");

        // Add other macro rules here...
        // CopyIfMissingOrZero(result, "SOMEKEY", "OTHERKEY");
        // ComputeDerived(...)

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

        // Copy entire Dimension object (nominal + tol + unit). This matches macro intention most of the time.
        // If you need to copy only nominal and preserve tol, adjust here.
        dims[tKey] = sDim;
    }

    private static bool IsZeroNominal(Dimension dim)
    {
        // Use your domain API. In your code you used dim.Nominal.Value sometimes.
        // Keep it numeric and unit-agnostic. "0" means "not provided" in macro logic.
        var v = (double)dim.Nominal.Value;
        return Math.Abs(v) <= Eps;
    }
}

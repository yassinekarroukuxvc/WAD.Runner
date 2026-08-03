using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.ModelAutomation.Core;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class Osg7EquationPlanner : StandardEquationPlanner
{
    private const decimal ProvidedValueEpsilon = 0.000000001m;
    private const double MinimumCosineMagnitude = 1e-12;

    /*
     * OSG7 uses a fixed TL value.
     *
     * The database TL value is intentionally overridden.
     */
    private const double Osg7TlMillimeters = 63.5;

    public override EquationPlan Build(
        ModelAutomationContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var wedge = context.Wedge
            ?? throw new InvalidOperationException(
                "WedgeData is required to build OSG7 equations.");

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        var builder = new EquationPlanBuilder()
            .WithDimensions(
                dimensions,
                EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions();

        /*
         * OSG7 TL override:
         *
         * Always force:
         *
         *     TL = 63.5 mm
         *
         * This intentionally replaces the value supplied by
         * the database for OSG7 models.
         */
        builder.AddManaged(
            "TL",
            EquationFormatting.LengthLineFromMillimeters(
                "TL",
                Osg7TlMillimeters));

        Logger.Info(
            "[Osg7EquationPlanner] OSG7 TL override applied. " +
            $"Using TL = {Osg7TlMillimeters} mm.");

        /*
         * FRA and BRA are not available in the database yet.
         *
         * Current fallback rules:
         *
         *     FRA = FA
         *     BRA = BA
         *
         * When valid FRA/BRA database values are introduced,
         * they will automatically take priority.
         */
        AddAngleCopyIfMissing(
            builder,
            dimensions,
            targetKey: "FRA",
            sourceKey: "FA");

        AddAngleCopyIfMissing(
            builder,
            dimensions,
            targetKey: "BRA",
            sourceKey: "BA");

        /*
         * FRX and BRX priority:
         *
         * 1. Use the database value when it exists and is non-zero.
         * 2. Otherwise calculate:
         *
         *    FRX = FR × (sec(FA) - tan(FA))
         *    BRX = BR × (sec(BA) - tan(BA))
         */
        AddCalculatedTaperPositionIfMissing(
            builder,
            dimensions,
            targetKey: "FRX",
            radiusKey: "FR",
            angleKey: "FA");

        AddCalculatedTaperPositionIfMissing(
            builder,
            dimensions,
            targetKey: "BRX",
            radiusKey: "BR",
            angleKey: "BA");

        /*
         * X priority:
         *
         * 1. Use X from the database when it exists and is non-zero.
         * 2. Otherwise calculate:
         *
         *    X = TDF - (FL + FX)
         */
        AddCalculatedXIfMissing(
            builder,
            dimensions);

        AddEngravingStart(
            builder,
            context);

        AddOverlayScale(
            builder,
            context);

        return builder.Build();
    }

    private static void AddAngleCopyIfMissing(
        EquationPlanBuilder builder,
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string targetKey,
        string sourceKey)
    {
        if (HasProvidedAngle(
                dimensions,
                targetKey,
                out var providedDegrees))
        {
            Logger.Info(
                $"[Osg7EquationPlanner] {targetKey} is present " +
                $"in the database ({providedDegrees} deg). " +
                "Using the database value.");

            return;
        }

        var sourceDegrees =
            RequireAngleDegrees(
                dimensions,
                sourceKey,
                targetKey);

        builder.AddManaged(
            targetKey,
            EquationFormatting.Line(
                targetKey,
                sourceDegrees,
                "deg"));

        Logger.Info(
            $"[Osg7EquationPlanner] {targetKey} is missing or zero. " +
            $"Using {targetKey} = {sourceKey} = " +
            $"{sourceDegrees} deg.");
    }

    private static void AddCalculatedTaperPositionIfMissing(
        EquationPlanBuilder builder,
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string targetKey,
        string radiusKey,
        string angleKey)
    {
        if (HasProvidedLength(
                dimensions,
                targetKey,
                out var providedMillimeters))
        {
            Logger.Info(
                $"[Osg7EquationPlanner] {targetKey} is present " +
                $"in the database ({providedMillimeters} mm). " +
                "Using the database value.");

            return;
        }

        var radiusMillimeters =
            RequireLengthMillimeters(
                dimensions,
                radiusKey,
                targetKey);

        var angleDegrees =
            RequireAngleDegrees(
                dimensions,
                angleKey,
                targetKey);

        var angleRadians =
            angleDegrees * Math.PI / 180.0;

        var cosine =
            Math.Cos(angleRadians);

        if (Math.Abs(cosine) < MinimumCosineMagnitude)
        {
            throw new InvalidOperationException(
                $"Cannot calculate {targetKey}. " +
                $"The angle {angleKey}={angleDegrees} deg " +
                "produces an invalid secant value.");
        }

        var secant =
            1.0 / cosine;

        var tangent =
            Math.Tan(angleRadians);

        var calculatedMillimeters =
            radiusMillimeters *
            (secant - tangent);

        if (!double.IsFinite(calculatedMillimeters))
        {
            throw new InvalidOperationException(
                $"Cannot calculate {targetKey}. " +
                $"The result from {radiusKey}={radiusMillimeters} mm " +
                $"and {angleKey}={angleDegrees} deg is invalid.");
        }

        builder.AddManaged(
            targetKey,
            EquationFormatting.LengthLineFromMillimeters(
                targetKey,
                calculatedMillimeters));

        Logger.Info(
            $"[Osg7EquationPlanner] {targetKey} is missing or zero. " +
            $"Calculated {targetKey} = {radiusKey} × " +
            $"(sec({angleKey}) - tan({angleKey})) = " +
            $"{calculatedMillimeters} mm.");
    }

    private static void AddCalculatedXIfMissing(
        EquationPlanBuilder builder,
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions)
    {
        const string targetKey = "X";

        /*
         * HasProvidedLength returns false when:
         *
         * - X does not exist.
         * - X is not a millimeter dimension.
         * - X is zero.
         * - X is extremely close to zero.
         */
        if (HasProvidedLength(
                dimensions,
                targetKey,
                out var providedMillimeters))
        {
            Logger.Info(
                "[Osg7EquationPlanner] X is present " +
                $"in the database ({providedMillimeters} mm). " +
                "Using the database value.");

            return;
        }

        var tdfMillimeters =
            RequireLengthMillimeters(
                dimensions,
                sourceKey: "TDF",
                calculatedKey: targetKey);

        var flMillimeters =
            RequireLengthMillimeters(
                dimensions,
                sourceKey: "FL",
                calculatedKey: targetKey);

        var fxMillimeters =
            RequireLengthMillimeters(
                dimensions,
                sourceKey: "FX",
                calculatedKey: targetKey);

        var calculatedMillimeters =
            tdfMillimeters -
            (flMillimeters + fxMillimeters);

        if (!double.IsFinite(calculatedMillimeters))
        {
            throw new InvalidOperationException(
                "Cannot calculate X because the result is invalid. " +
                $"TDF={tdfMillimeters} mm, " +
                $"FL={flMillimeters} mm, " +
                $"FX={fxMillimeters} mm.");
        }

        builder.AddManaged(
            targetKey,
            EquationFormatting.LengthLineFromMillimeters(
                targetKey,
                calculatedMillimeters));

        Logger.Info(
            "[Osg7EquationPlanner] X is missing or zero. " +
            "Calculated X = TDF - (FL + FX) = " +
            $"{tdfMillimeters} - " +
            $"({flMillimeters} + {fxMillimeters}) = " +
            $"{calculatedMillimeters} mm.");
    }

    private static bool HasProvidedLength(
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string key,
        out double millimeters)
    {
        millimeters = 0.0;

        if (!TryGetDimension(
                dimensions,
                key,
                out var dimension))
        {
            return false;
        }

        if (dimension.Nominal.Unit != UnitKind.Millimeter)
            return false;

        var value =
            dimension.Nominal.AsMm();

        /*
         * Zero and near-zero values are treated as not provided.
         *
         * Therefore, X=0 causes AddCalculatedXIfMissing to calculate:
         *
         *     X = TDF - (FL + FX)
         */
        if (decimal.Abs(value) <= ProvidedValueEpsilon)
            return false;

        millimeters = (double)value;

        return double.IsFinite(millimeters);
    }

    private static bool HasProvidedAngle(
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string key,
        out double degrees)
    {
        degrees = 0.0;

        if (!TryGetDimension(
                dimensions,
                key,
                out var dimension))
        {
            return false;
        }

        if (dimension.Nominal.Unit != UnitKind.Degree)
            return false;

        var value =
            dimension.Nominal.AsDeg();

        if (decimal.Abs(value) <= ProvidedValueEpsilon)
            return false;

        degrees = (double)value;

        return double.IsFinite(degrees);
    }

    private static double RequireLengthMillimeters(
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string sourceKey,
        string calculatedKey)
    {
        if (!TryGetDimension(
                dimensions,
                sourceKey,
                out var dimension))
        {
            throw new InvalidOperationException(
                $"Cannot calculate {calculatedKey} because " +
                $"the required dimension '{sourceKey}' is missing.");
        }

        if (dimension.Nominal.Unit != UnitKind.Millimeter)
        {
            throw new InvalidOperationException(
                $"Cannot calculate {calculatedKey} because " +
                $"'{sourceKey}' must be a millimeter dimension, " +
                $"but its unit is {dimension.Nominal.Unit}.");
        }

        var millimeters =
            (double)dimension.Nominal.AsMm();

        if (!double.IsFinite(millimeters))
        {
            throw new InvalidOperationException(
                $"Cannot calculate {calculatedKey} because " +
                $"'{sourceKey}' has an invalid value.");
        }

        return millimeters;
    }

    private static double RequireAngleDegrees(
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string sourceKey,
        string calculatedKey)
    {
        if (!TryGetDimension(
                dimensions,
                sourceKey,
                out var dimension))
        {
            throw new InvalidOperationException(
                $"Cannot calculate {calculatedKey} because " +
                $"the required angle '{sourceKey}' is missing.");
        }

        if (dimension.Nominal.Unit != UnitKind.Degree)
        {
            throw new InvalidOperationException(
                $"Cannot calculate {calculatedKey} because " +
                $"'{sourceKey}' must be an angle in degrees, " +
                $"but its unit is {dimension.Nominal.Unit}.");
        }

        var degrees =
            (double)dimension.Nominal.AsDeg();

        if (!double.IsFinite(degrees))
        {
            throw new InvalidOperationException(
                $"Cannot calculate {calculatedKey} because " +
                $"'{sourceKey}' has an invalid value.");
        }

        return degrees;
    }

    private static bool TryGetDimension(
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string key,
        out DomDim dimension)
    {
        dimension = null!;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!dimensions.TryGetValue(
                DimensionKey.From(key),
                out var resolved) ||
            resolved is null)
        {
            /*
             * Secondary case-insensitive search in case DimensionKey
             * equality does not normalize capitalization.
             */
            foreach (var pair in dimensions)
            {
                if (!string.Equals(
                        pair.Key.Value,
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                resolved = pair.Value;
                break;
            }
        }

        if (resolved is null)
            return false;

        dimension = resolved;
        return true;
    }
}
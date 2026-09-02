using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

/// <summary>
/// Builds the equations used by the CKVD model.
///
/// CKVD-specific rules:
///
/// 1. The CKVD shank/construction style is read from the Parts
///    Specification property "Wed-Type":
///
///       LW_STYLE_A_CKVD -> Style A
///       LW_STYLE_B_CKVD -> Style B
///
/// 2. FRX and BRX use their database values when they are present
///    and non-zero. Otherwise they are calculated from:
///
///       FRX = FR * (sec(FA) - tan(FA))
///       BRX = BR * (sec(BA) - tan(BA))
///
///    For CKVD only, FA and BA are considered to be 0 degrees inside
///    these fallback calculations. The actual FA and BA equation
///    values supplied by the database are not overwritten.
///
/// 3. Style B uses X and FX. When X is missing or zero, calculate:
///
///       X = TDF - (FL + FX)
///
///    Style A does not use the X/FX construction, so this fallback is
///    not applied to Style A.
/// </summary>
public sealed class CkvdEquationPlanner : StandardEquationPlanner
{
    private const decimal ProvidedValueEpsilon = 0.000000001m;
    private const double MinimumCosineMagnitude = 1e-12;
    private readonly WedgeType _wedgeType;
    private string non_std_cut_equation_name = "non_std_cut";

    private const double CkvdProjectionAngleDegrees = 0.0;

    public CkvdEquationPlanner(WedgeType wedgeType)
    {
        _wedgeType = wedgeType;
    }

    public override EquationPlan Build(
        ModelAutomationContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var wedge = context.Wedge
            ?? throw new InvalidOperationException(
                "WedgeData is required to build CKVD equations.");

        var facts = context.Facts
            ?? new WedgeFacts(wedge);

        var shankStyle = ResolveShankStyle(facts);

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        var builder = new EquationPlanBuilder()
            .WithDimensions(
                dimensions,
                EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions();

        Logger.Info(
            "[CkvdEquationPlanner] CKVD shank style resolved " +
            $"from Wed-Type: {shankStyle}.");

        AddCalculatedProjectionIfMissing(
            builder,
            dimensions,
            targetKey: "FRX",
            radiusKey: "FR",
            angleNameForLog: "FA");

        AddCalculatedProjectionIfMissing(
            builder,
            dimensions,
            targetKey: "BRX",
            radiusKey: "BR",
            angleNameForLog: "BA");

        if (shankStyle == CkvdShankStyle.StyleB || shankStyle == CkvdShankStyle.StyleA)
        {
            AddCalculatedXIfMissing(
                builder,
                dimensions);
        }

        if (context.DrawingType == DrawingType.Overlay)
        {
            AddOverlayDimensionOverrides(
                builder,
                facts,
                context.Subclass);
        }

        AddEngravingStart(
            builder,
            context);

        AddOverlayScale(
            builder,
            context);

        var rawCut = EquationGeometry.NonStdCutRawMm(facts);
        var finalCut = rawCut;
        if (context.DrawingType == DrawingType.Overlay)
        {
            var mag = EquationGeometry.OverlayMagnification(facts, _wedgeType);
            var scale = EquationGeometry.OverlayScaleDecimal(mag);
            finalCut = EquationGeometry.OverlaySafeNonStdCutMm(rawCut, scale, _wedgeType);
        }

        builder.AddManaged(
            non_std_cut_equation_name,
            EquationFormatting.LengthLineFromMillimeters(
                non_std_cut_equation_name,
                finalCut));

        return builder.Build();
    }

    private static void AddOverlayDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        var hasOverlayVrFamily = HasAnyPositiveNominal(
            facts,
            "VR",
            "VRR",
            "VW");

        var overlayVwCase = ResolveOverlayVwCase(
            facts,
            hasOverlayVrFamily);

        if (subclass == WedgeSubclass.PGB)
        {
            if (!hasOverlayVrFamily)
            {
                AddLengthBoundEquation(
                    builder,
                    facts,
                    "W",
                    useMaximum: true);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    "FL",
                    useMaximum: true);
            }
            else
            {
                AddLengthBoundEquation(
                    builder,
                    facts,
                    "FL",
                    useMaximum: false);

                AddVrFamilyMaximumEquations(
                    builder,
                    facts);

                if (overlayVwCase == OverlayVwCase.Case2)
                {
                    AddCase2MaximumEquations(
                        builder,
                        facts);
                }
            }
        }
        else
        {
            if (!hasOverlayVrFamily)
            {
                AddLengthBoundEquation(
                    builder,
                    facts,
                    "B",
                    useMaximum: false);

                AddAngleBoundEquation(
                    builder,
                    facts,
                    "GA",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    "GD",
                    useMaximum: false);
            }
            else
            {
                AddVrFamilyMaximumEquations(
                    builder,
                    facts);

                if (overlayVwCase == OverlayVwCase.Case2)
                {
                    AddCase2MaximumEquations(
                        builder,
                        facts);
                }
            }
        }

        Logger.Info(
            "[CkvdEquationPlanner] Overlay dimension overrides -> " +
            $"subclass={subclass}, " +
            $"VR/VRR/VW present={hasOverlayVrFamily}, " +
            $"VW case={overlayVwCase}.");
    }

    private static void AddVrFamilyMaximumEquations(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        AddLengthBoundEquation(
            builder,
            facts,
            "VW",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            "VR",
            useMaximum: true);

        AddAngleBoundEquation(
            builder,
            facts,
            "VRA",
            useMaximum: true);
    }

    private static void AddCase2MaximumEquations(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        AddLengthBoundEquation(
            builder,
            facts,
            "W",
            useMaximum: true);

        AddAngleBoundEquation(
            builder,
            facts,
            "ISA",
            useMaximum: true);
    }

    private static void AddLengthBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minimumMillimeters,
                out var maximumMillimeters))
        {
            Logger.Warn(
                "[CkvdEquationPlanner] Missing/invalid nominal or " +
                $"tolerance for '{dimensionKey}'. The requested " +
                $"{(useMaximum ? "maximum" : "minimum")} overlay " +
                "equation override was skipped; the existing nominal " +
                "equation remains in effect.");

            return;
        }

        var selectedMillimeters =
            useMaximum
                ? maximumMillimeters
                : minimumMillimeters;

        builder.AddManaged(
            dimensionKey,
            EquationFormatting.LengthLineFromMillimeters(
                dimensionKey,
                selectedMillimeters));

        Logger.Info(
            "[CkvdEquationPlanner] Overlay bound equation -> " +
            $"{dimensionKey}={(useMaximum ? "MAX" : "MIN")}, " +
            $"value={selectedMillimeters} mm.");
    }

    private static void AddAngleBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetAngleBoundsDeg(
                dimensionKey,
                out var minimumDegrees,
                out var maximumDegrees))
        {
            Logger.Warn(
                "[CkvdEquationPlanner] Missing/invalid nominal or " +
                $"tolerance for angular dimension '{dimensionKey}'. " +
                $"The requested {(useMaximum ? "maximum" : "minimum")} " +
                "overlay equation override was skipped; the existing " +
                "nominal equation remains in effect.");

            return;
        }

        var selectedDegrees =
            useMaximum
                ? maximumDegrees
                : minimumDegrees;

        builder.AddManaged(
            dimensionKey,
            EquationFormatting.Line(
                dimensionKey,
                selectedDegrees,
                "deg"));

        Logger.Info(
            "[CkvdEquationPlanner] Overlay angular bound equation -> " +
            $"{dimensionKey}={(useMaximum ? "MAX" : "MIN")}, " +
            $"value={selectedDegrees} deg.");
    }

    private static bool HasAnyPositiveNominal(
        WedgeFacts facts,
        params string[] dimensionKeys)
    {
        foreach (var key in dimensionKeys)
        {
            if (facts.HasPositive(key))
                return true;
        }

        return false;
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasOverlayVrFamily)
    {
        if (!hasOverlayVrFamily ||
            !facts.TryGetLengthMm(
                "VW",
                out var vwMillimeters) ||
            vwMillimeters <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[CkvdEquationPlanner] VW is present but W is missing " +
                "or not a length. CKVD Case 1/Case 2 equation selection " +
                "could not be resolved.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        if (vwMillimeters >
            wMillimeters + WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case2;
        }

        Logger.Warn(
            "[CkvdEquationPlanner] CKVD overlay received VW < W " +
            $"(VW={vwMillimeters} mm, W={wMillimeters} mm). " +
            "Only VW = W (Case 1) and VW > W (Case 2) are defined.");

        return OverlayVwCase.None;
    }

    private static CkvdShankStyle ResolveShankStyle(
        WedgeFacts facts)
    {
        if (facts is null)
            throw new ArgumentNullException(nameof(facts));

        var raw = facts.NormalizedPropertyToken(
            "Wed-Type",
            "Wed_Type",
            "Wed Type",
            "Shank_Type",
            "shank_type");

        if (string.Equals(
                raw,
                "LW_STYLE_A_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdShankStyle.StyleA;
        }

        if (string.Equals(
                raw,
                "LW_STYLE_B_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdShankStyle.StyleB;
        }

        throw new InvalidOperationException(
            "Cannot resolve the CKVD shank style from 'Wed-Type'. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{(string.IsNullOrWhiteSpace(raw) ? "<missing>" : raw)}'.");
    }

    private static void AddCalculatedProjectionIfMissing(
        EquationPlanBuilder builder,
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        string targetKey,
        string radiusKey,
        string angleNameForLog)
    {
        if (HasProvidedLength(
                dimensions,
                targetKey,
                out var providedMillimeters))
        {
            Logger.Info(
                $"[CkvdEquationPlanner] {targetKey} is present " +
                $"in the database ({providedMillimeters} mm). " +
                "Using the database value.");

            return;
        }

        var radiusMillimeters =
            RequireLengthMillimeters(
                dimensions,
                radiusKey,
                targetKey);

        /*
         * FA=0 degrees and BA=0 degrees only
         * for the CKVD FRX/BRX fallback calculations.
         */
        var angleRadians =
            CkvdProjectionAngleDegrees *
            Math.PI /
            180.0;

        var cosine =
            Math.Cos(angleRadians);

        if (Math.Abs(cosine) < MinimumCosineMagnitude)
        {
            throw new InvalidOperationException(
                $"Cannot calculate {targetKey}. " +
                $"The CKVD calculation angle for {angleNameForLog} " +
                $"({CkvdProjectionAngleDegrees} deg) produces an " +
                "invalid secant value.");
        }

        var calculatedMillimeters =
            radiusMillimeters *
            ((1.0 / cosine) - Math.Tan(angleRadians));

        if (!double.IsFinite(calculatedMillimeters))
        {
            throw new InvalidOperationException(
                $"Cannot calculate {targetKey}. " +
                $"The result from {radiusKey}={radiusMillimeters} mm " +
                "is invalid.");
        }

        builder.AddManaged(
            targetKey,
            EquationFormatting.LengthLineFromMillimeters(
                targetKey,
                calculatedMillimeters));

        Logger.Info(
            $"[CkvdEquationPlanner] {targetKey} is missing or zero. " +
            $"Calculated {targetKey} = {radiusKey} * " +
            $"(sec({angleNameForLog}) - tan({angleNameForLog})) " +
            $"with CKVD {angleNameForLog}=0 deg: " +
            $"{calculatedMillimeters} mm.");
    }

    private static void AddCalculatedXIfMissing(
        EquationPlanBuilder builder,
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions)
    {
        const string targetKey = "X";

        if (HasProvidedLength(
                dimensions,
                targetKey,
                out var providedMillimeters))
        {
            Logger.Info(
                "[CkvdEquationPlanner] X is present " +
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
                "Cannot calculate CKVD X because the result is invalid. " +
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
            "[CkvdEquationPlanner] Style B X is missing or zero. " +
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
         * CKVD treats zero and near-zero FRX, BRX and X values as
         * missing so the requested fallback equation can be applied.
         */
        if (decimal.Abs(value) <= ProvidedValueEpsilon)
            return false;

        millimeters =
            (double)value;

        return double.IsFinite(millimeters);
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
                $"the required CKVD dimension '{sourceKey}' is missing.");
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

    private enum CkvdShankStyle
    {
        StyleA,
        StyleB
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}

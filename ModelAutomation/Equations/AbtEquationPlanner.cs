using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

/// <summary>
/// Builds the equations used by the ABT model.
///
/// ABT PGB overlay:
///     W     = W_MAX
///
/// ABT FG overlay:
///     W     = nominal value
///
/// Both PGB and FG overlay:
///     T     = T_MAX
///     FD    = FD_MAX
///     RA2H  = RA2H_MAX when RA2H > 0
///
/// When VR > 0 and VW > 0:
///     VW    = VW_MAX
///     VR    = VR_MIN
///     VRA   = VRA_MAX
///
/// Case 1:
///     VW = W
///
/// Case 2:
///     VW != W
///     ISA = ISA_MAX
///
/// Foot depth:
///     LW_VG / SW_VG -> foot_depth = GD
///     LW_G / SW_G   -> foot_depth = GD
///     LW_C / SW_C   -> foot_depth = CD
///     Other         -> foot_depth = 0
///
/// C with CBR is not a separate foot-option token.
/// LW_C / SW_C is treated as C-with-CBR when CBR > 0.
///
/// FG overlay foot overrides:
///     LW_C / SW_C   -> CL_MIN, CD_MIN
///     LW_VG / SW_VG -> GA_MIN, GD_MIN, B_MIN
///     LW_G / SW_G   -> GO_MIN, GD_MIN
/// </summary>
public sealed class AbtEquationPlanner : StandardEquationPlanner
{
    private const string FootDepthEquationName =
        "foot_depth";

    private const string NonStdCutEquationName =
        "non_std_cut";

    private readonly WedgeType _wedgeType;

    public AbtEquationPlanner(
        WedgeType wedgeType)
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
                "WedgeData is required to build ABT equations.");

        var facts = context.Facts
            ?? new WedgeFacts(wedge);

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        var builder = new EquationPlanBuilder()
            .WithDimensions(
                dimensions,
                EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions();

        AddFootDepthEquation(
            builder,
            facts);

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

        AddNonStandardCutEquation(
            builder,
            facts,
            context.DrawingType);

        return builder.Build();
    }

    // ================================================================
    // OVERLAY DIMENSION OVERRIDES
    // ================================================================

    private static void AddOverlayDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        // PGB only:
        // W = W_MAX.
        //
        // FG:
        // Do not override W.
        // The nominal W value loaded through WithDimensions()
        // remains active.
        if (subclass == WedgeSubclass.PGB)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                dimensionKey: "W",
                useMaximum: true);
        }

        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "T",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "FD",
            useMaximum: true);

        if (facts.HasPositive("RA2H"))
        {
            AddLengthBoundEquation(
                builder,
                facts,
                dimensionKey: "RA2H",
                useMaximum: true);
        }

        var hasVrVw =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW");

        var overlayVwCase =
            ResolveOverlayVwCase(
                facts,
                hasVrVw);

        if (hasVrVw)
        {
            AddVrVwOverlayOverrides(
                builder,
                facts);

            if (overlayVwCase == OverlayVwCase.Case2)
            {
                AddAngleBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "ISA",
                    useMaximum: true);
            }
        }

        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOverlayOverrides(
                builder,
                facts);
        }

        Logger.Info(
            "[AbtEquationPlanner] Overlay dimension overrides -> " +
            $"subclass={subclass}, " +
            $"W override={(subclass == WedgeSubclass.PGB ? "MAX" : "NOMINAL")}, " +
            $"VR/VW present={hasVrVw}, " +
            $"VW case={overlayVwCase}, " +
            $"RA2H present={facts.HasPositive("RA2H")}, " +
            $"foot option={ResolveFootKind(facts, ResolveNormalizedFootOption(facts))}.");
    }

    private static void AddVrVwOverlayOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "VW",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "VR",
            useMaximum: false);

        AddAngleBoundEquation(
            builder,
            facts,
            dimensionKey: "VRA",
            useMaximum: true);
    }

    private static void AddFgFootOverlayOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var normalizedFootOption =
            ResolveNormalizedFootOption(
                facts);

        var footKind =
            ResolveFootKind(
                facts,
                normalizedFootOption);

        switch (footKind)
        {
            case FootKind.C:
            case FootKind.CWithCbr:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "CL",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "CD",
                    useMaximum: false);

                break;

            case FootKind.Vg:
                AddAngleBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GA",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GD",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "B",
                    useMaximum: false);

                break;

            case FootKind.G:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GO",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GD",
                    useMaximum: false);

                break;

            case FootKind.CC:
            case FootKind.Unknown:
            default:
                break;
        }

        Logger.Info(
            "[AbtEquationPlanner] FG overlay foot overrides -> " +
            $"raw='{DisplayToken(normalizedFootOption)}', " +
            $"resolved={footKind}.");
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
                "[AbtEquationPlanner] Missing or invalid nominal/" +
                $"tolerance for length dimension '{dimensionKey}'. " +
                $"The requested {(useMaximum ? "maximum" : "minimum")} " +
                "overlay override was skipped. The nominal equation " +
                "remains active.");

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
            "[AbtEquationPlanner] Overlay length bound -> " +
            $"{dimensionKey}=" +
            $"{(useMaximum ? "MAX" : "MIN")}, " +
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
                "[AbtEquationPlanner] Missing or invalid nominal/" +
                $"tolerance for angle dimension '{dimensionKey}'. " +
                $"The requested {(useMaximum ? "maximum" : "minimum")} " +
                "overlay override was skipped. The nominal equation " +
                "remains active.");

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
            "[AbtEquationPlanner] Overlay angle bound -> " +
            $"{dimensionKey}=" +
            $"{(useMaximum ? "MAX" : "MIN")}, " +
            $"value={selectedDegrees} deg.");
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vwMillimeters) ||
            vwMillimeters <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[AbtEquationPlanner] VW is present but W is " +
                "missing or not a length. The VW overlay case " +
                "could not be resolved.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        return OverlayVwCase.Case2;
    }

    private static bool HasAllPositiveNominal(
        WedgeFacts facts,
        params string[] dimensionKeys)
    {
        foreach (var key in dimensionKeys)
        {
            if (!facts.HasPositive(key))
                return false;
        }

        return true;
    }

    // ================================================================
    // FOOT DEPTH
    // ================================================================

    private static void AddFootDepthEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var footOption =
            ResolveNormalizedFootOption(
                facts);

        var footKind =
            ResolveFootKind(
                facts,
                footOption);

        decimal footDepthMm;
        string sourceDescription;

        switch (footKind)
        {
            case FootKind.Vg:
            case FootKind.G:
                footDepthMm =
                    RequireFootDepthSource(
                        facts,
                        sourceDimension: "GD",
                        footOption);

                sourceDescription =
                    "GD";

                break;

            case FootKind.C:
            case FootKind.CWithCbr:
                footDepthMm =
                    RequireFootDepthSource(
                        facts,
                        sourceDimension: "CD",
                        footOption);

                sourceDescription =
                    "CD";

                break;

            case FootKind.CC:
            case FootKind.Unknown:
            default:
                footDepthMm =
                    0m;

                sourceDescription =
                    "0";

                break;
        }

        builder.AddManaged(
            FootDepthEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FootDepthEquationName,
                footDepthMm));

        Logger.Info(
            "[AbtEquationPlanner] Foot depth resolved -> " +
            $"Wed-Foot_Option='{DisplayToken(footOption)}', " +
            $"foot kind={footKind}, " +
            $"source={sourceDescription}, " +
            $"foot_depth={footDepthMm} mm.");
    }

    private static decimal RequireFootDepthSource(
        WedgeFacts facts,
        string sourceDimension,
        string footOption)
    {
        if (!facts.TryGetLengthMm(
                sourceDimension,
                out var valueMm))
        {
            throw new InvalidOperationException(
                "Cannot calculate ABT foot_depth. " +
                $"Wed-Foot_Option '{DisplayToken(footOption)}' " +
                $"requires dimension '{sourceDimension}', " +
                "but that dimension is missing or is not a " +
                "millimeter dimension.");
        }

        if (valueMm < 0m)
        {
            throw new InvalidOperationException(
                "Cannot calculate ABT foot_depth. " +
                $"Dimension '{sourceDimension}' has an invalid " +
                $"negative value: {valueMm} mm.");
        }

        return valueMm;
    }

    // ================================================================
    // FOOT OPTION
    // ================================================================

    private static string ResolveNormalizedFootOption(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Foot_Option",
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Wed-Foot Option",
                "Foot_Option",
                "Foot Option",
                "foot_option");

        return NormalizePackedToken(
            raw);
    }

    private static FootKind ResolveFootKind(
        WedgeFacts facts,
        string normalizedFootOption)
    {
        return normalizedFootOption switch
        {
            "LW_C" or
            "SW_C" =>
                facts.HasPositive("CBR")
                    ? FootKind.CWithCbr
                    : FootKind.C,

            "LW_VG" or
            "SW_VG" =>
                FootKind.Vg,

            "LW_G" or
            "SW_G" =>
                FootKind.G,

            "LW_CC" or
            "SW_CC" =>
                FootKind.CC,

            _ =>
                FootKind.Unknown
        };
    }

    // ================================================================
    // NON-STANDARD CUT
    // ================================================================

    private void AddNonStandardCutEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        DrawingType drawingType)
    {
        var rawCut =
            EquationGeometry.NonStdCutRawMm(
                facts);

        var finalCut =
            rawCut;

        if (drawingType == DrawingType.Overlay)
        {
            var magnification =
                EquationGeometry.OverlayMagnification(
                    facts,
                    _wedgeType);

            var scale =
                EquationGeometry.OverlayScaleDecimal(
                    magnification);

            finalCut =
                EquationGeometry.OverlaySafeNonStdCutMm(
                    rawCut,
                    scale,
                    _wedgeType);
        }

        builder.AddManaged(
            NonStdCutEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                NonStdCutEquationName,
                finalCut));
    }

    // ================================================================
    // TOKEN NORMALIZATION
    // ================================================================

    private static string NormalizePackedToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            raw
                .Trim()
                .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        if (separatorIndex >= 0)
        {
            token =
                token[..separatorIndex];
        }

        token =
            token
                .Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .Trim('_')
                .ToUpperInvariant();

        while (token.Contains(
                   "__",
                   StringComparison.Ordinal))
        {
            token =
                token.Replace(
                    "__",
                    "_",
                    StringComparison.Ordinal);
        }

        return token;
    }

    private static string DisplayToken(
        string token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;
    }

    private enum FootKind
    {
        Unknown,
        C,
        CWithCbr,
        G,
        CC,
        Vg
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
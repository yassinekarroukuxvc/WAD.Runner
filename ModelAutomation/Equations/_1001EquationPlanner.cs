using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class _1001EquationPlanner : StandardEquationPlanner
{
    private const string FootDepthEquationName =
        "foot_depth";

    private const string NonStdCutEquationName =
        "non_std_cut";

    public override EquationPlan Build(
        ModelAutomationContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var wedge = context.Wedge
            ?? throw new InvalidOperationException(
                "WedgeData is required to build _1001 equations.");

        var facts =
            context.Facts ??
            new WedgeFacts(wedge);

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        var builder =
            new EquationPlanBuilder()
                .WithDimensions(
                    dimensions,
                    EquationCatalog.DbToModelAliases)
                .SkipProvidedZeroDimensions();

        AddFootDepthEquation(
            builder,
            facts);

        AddFunnelGapEquation(
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

    private static void AddFunnelGapEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var funnelGap =
            EquationGeometry.FunnelGapMmOrDefault(
                facts);

        builder.AddManaged(
            EquationCatalog.Names.FunnelGap,
            EquationFormatting.LengthLineFromMillimeters(
                EquationCatalog.Names.FunnelGap,
                funnelGap));

        Logger.Info(
            $"[_1001EquationPlanner] funnel_gap={funnelGap} mm.");
    }

    private static void AddOverlayDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "W",
                useMaximum: true);
        }

        AddLengthBoundEquation(
            builder,
            facts,
            "T",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            "FL",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            "ND",
            useMaximum: false);

        var hasVbl =
            facts.HasPositive(
                "VBL");

        var hasRa2 =
            facts.HasPositive(
                "RA2");

        if ((!hasVbl && !hasRa2) ||
            hasVbl)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "C",
                useMaximum: false);
        }

        if (hasRa2)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "RA2H",
                useMaximum: false);
        }

        var hasVrVw =
            HasAllPositive(
                facts,
                "VR",
                "VW");

        var vwCase =
            ResolveOverlayVwCase(
                facts,
                hasVrVw);

        if (hasVrVw)
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
                useMaximum: false);

            AddAngleBoundEquation(
                builder,
                facts,
                "VRA",
                useMaximum: true);

            if (vwCase ==
                OverlayVwCase.Case2)
            {
                AddAngleBoundEquation(
                    builder,
                    facts,
                    "ISA",
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
            "[_1001EquationPlanner] Overlay overrides -> " +
            $"subclass={subclass}, " +
            $"W={(subclass == WedgeSubclass.PGB ? "MAX" : "NOMINAL")}, " +
            $"VR/VW={hasVrVw}, " +
            $"VW case={vwCase}, " +
            $"VBL={hasVbl}, " +
            $"RA2={hasRa2}, " +
            $"foot={ResolveFootKind(ResolveNormalizedFootOption(facts))}.");
    }

    private static void AddFgFootOverlayOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var footOption =
            ResolveNormalizedFootOption(
                facts);

        var footKind =
            ResolveFootKind(
                footOption);

        switch (footKind)
        {
            case FootKind.C:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    "CL",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    "CD",
                    useMaximum: false);

                break;

            case FootKind.Vg:
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

                AddLengthBoundEquation(
                    builder,
                    facts,
                    "B",
                    useMaximum: false);

                break;

            case FootKind.G:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    "GO",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    "GD",
                    useMaximum: false);

                break;

            case FootKind.Cc:
            case FootKind.Unknown:
            default:
                break;
        }
    }

    private static void AddFootDepthEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var footOption =
            ResolveNormalizedFootOption(
                facts);

        var footKind =
            ResolveFootKind(
                footOption);

        decimal footDepthMm;
        string source;

        switch (footKind)
        {
            case FootKind.Vg:
            case FootKind.G:
                footDepthMm =
                    RequireLength(
                        facts,
                        "GD",
                        footOption);

                source =
                    "GD";

                break;

            case FootKind.C:
                footDepthMm =
                    RequireLength(
                        facts,
                        "CD",
                        footOption);

                source =
                    "CD";

                break;

            default:
                footDepthMm =
                    0m;

                source =
                    "0";

                break;
        }

        builder.AddManaged(
            FootDepthEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FootDepthEquationName,
                footDepthMm));

        Logger.Info(
            "[_1001EquationPlanner] Foot depth -> " +
            $"option='{DisplayToken(footOption)}', " +
            $"kind={footKind}, source={source}, " +
            $"value={footDepthMm} mm.");
    }

    private static decimal RequireLength(
        WedgeFacts facts,
        string dimensionKey,
        string footOption)
    {
        if (!facts.TryGetLengthMm(
                dimensionKey,
                out var valueMm))
        {
            throw new InvalidOperationException(
                "Cannot calculate _1001 foot_depth. " +
                $"Wed-Foot_Option '{DisplayToken(footOption)}' " +
                $"requires dimension '{dimensionKey}', " +
                "but it is missing or is not a millimeter dimension.");
        }

        if (valueMm < 0m)
        {
            throw new InvalidOperationException(
                "Cannot calculate _1001 foot_depth. " +
                $"'{dimensionKey}' is negative: {valueMm} mm.");
        }

        return valueMm;
    }

    private static void AddLengthBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minMm,
                out var maxMm))
        {
            Logger.Warn(
                $"[_1001EquationPlanner] Missing/invalid bounds for '{dimensionKey}'. " +
                $"{(useMaximum ? "MAX" : "MIN")} override skipped.");

            return;
        }

        var selected =
            useMaximum
                ? maxMm
                : minMm;

        builder.AddManaged(
            dimensionKey,
            EquationFormatting.LengthLineFromMillimeters(
                dimensionKey,
                selected));
    }

    private static void AddSourceLengthBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string targetEquationKey,
        string sourceDimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetLengthBoundsMm(
                sourceDimensionKey,
                out var minMm,
                out var maxMm))
        {
            Logger.Warn(
                $"[_1001EquationPlanner] Missing/invalid bounds for source '{sourceDimensionKey}'. " +
                $"Override '{targetEquationKey}=" +
                $"{(useMaximum ? sourceDimensionKey + "_MAX" : sourceDimensionKey + "_MIN")}' skipped.");

            return;
        }

        var selected =
            useMaximum
                ? maxMm
                : minMm;

        builder.AddManaged(
            targetEquationKey,
            EquationFormatting.LengthLineFromMillimeters(
                targetEquationKey,
                selected));
    }

    private static void AddAngleBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetAngleBoundsDeg(
                dimensionKey,
                out var minDeg,
                out var maxDeg))
        {
            Logger.Warn(
                $"[_1001EquationPlanner] Missing/invalid angle bounds for '{dimensionKey}'. " +
                $"{(useMaximum ? "MAX" : "MIN")} override skipped.");

            return;
        }

        var selected =
            useMaximum
                ? maxDeg
                : minDeg;

        builder.AddManaged(
            dimensionKey,
            EquationFormatting.Line(
                dimensionKey,
                selected,
                "deg"));
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vw) ||
            vw <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var w))
        {
            Logger.Warn(
                "[_1001EquationPlanner] VW is present but W is missing/not a length. " +
                "VW case unresolved.");

            return OverlayVwCase.None;
        }

        return decimal.Abs(vw - w) <=
               WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVwCase.Case1
            : OverlayVwCase.Case2;
    }

    private static bool HasAllPositive(
        WedgeFacts facts,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!facts.HasPositive(key))
                return false;
        }

        return true;
    }

    private static string ResolveNormalizedFootOption(
        WedgeFacts facts)
    {
        return NormalizePackedToken(
            facts.NormalizedPropertyToken(
                "Wed-Foot_Option",
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Wed-Foot Option",
                "Foot_Option",
                "Foot Option",
                "foot_option"));
    }

    private static FootKind ResolveFootKind(
        string footOption)
    {
        return footOption switch
        {
            "LW_C" or
            "SW_C" or
            "C" =>
                FootKind.C,

            "LW_VG" or
            "SW_VG" or
            "VG" =>
                FootKind.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                FootKind.G,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                FootKind.Cc,

            "LW_F" or "SW_F" or "F" => FootKind.F,

            _ =>
                FootKind.Unknown
        };
    }

    private static void AddNonStandardCutEquation(
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
                    WedgeType._1001);

            var scale =
                EquationGeometry.OverlayScaleDecimal(
                    magnification);

            finalCut =
                EquationGeometry.OverlaySafeNonStdCutMm(
                    rawCut,
                    scale,
                    WedgeType._1001);
        }

        builder.AddManaged(
            NonStdCutEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                NonStdCutEquationName,
                finalCut));
    }

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
        => string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;

    private enum FootKind
    {
        Unknown,
        C,
        Vg,
        G,
        Cc,
        F
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
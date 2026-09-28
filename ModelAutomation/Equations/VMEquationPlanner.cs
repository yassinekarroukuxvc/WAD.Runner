using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

using DomDim =
    WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class VMEquationPlanner : StandardEquationPlanner
{
    private const string FootDepthEquationName =
        "foot_depth";

    public override EquationPlan Build(
        ModelAutomationContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var wedge = context.Wedge
            ?? throw new InvalidOperationException(
                "WedgeData is required to build VM equations.");

        var facts =
            context.Facts ??
            new WedgeFacts(wedge, context.Subclass);

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        var builder =
            new EquationPlanBuilder()
                .WithDimensions(
                    dimensions,
                    EquationCatalog.DbToModelAliases)
                .SkipProvidedZeroDimensions();

        // Foot depth exists only for FG.
        if (context.Subclass == WedgeSubclass.FG)
        {
            AddFootDepthEquation(
                builder,
                facts);
        }

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
            $"[VMEquationPlanner] funnel_gap={funnelGap} mm.");
    }

    private static void AddOverlayDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        // PGB only:
        // W -> W_MAX
        if (subclass == WedgeSubclass.PGB)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "W",
                useMaximum: true);
        }

        // FG + PGB:
        // T  -> T_MAX
        // FL -> FL_MAX
        // ND -> ND_MIN
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

        var tCase =
            ResolveTCase(
                hasVbl,
                hasRa2);

        // T Case 1:
        // VBL <= 0 and RA2 <= 0
        //
        // T Case 2:
        // VBL > 0 and RA2 <= 0
        //
        // Both cases:
        // C -> C_MIN
        if (tCase is TCase.Case1 or TCase.Case2)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "C",
                useMaximum: false);
        }

        // T Case 3:
        // VBL <= 0 and RA2 > 0
        //
        // T Case 4:
        // VBL > 0 and RA2 > 0
        //
        // Both cases:
        // RA2H -> RA2H_MIN
        if (tCase is TCase.Case3 or TCase.Case4)
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
            // For both VW = W and VW != W:
            // VW  -> VW_MAX
            // VR  -> VR_MIN
            // VRA -> VRA_MAX
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

            // VW Case 2:
            // VW != W
            // ISA -> ISA_MAX
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

        // FG-only foot option tolerance overrides.
        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOverlayOverrides(
                builder,
                facts);
        }

        var footLog =
            subclass == WedgeSubclass.FG
                ? ResolveFootKind(
                    facts,
                    ResolveNormalizedFootOption(facts)).ToString()
                : "N/A";

        Logger.Info(
            "[VMEquationPlanner] Overlay overrides -> " +
            $"subclass={subclass}, " +
            $"W={(subclass == WedgeSubclass.PGB ? "MAX" : "NOMINAL")}, " +
            $"VR/VW={hasVrVw}, " +
            $"VW case={vwCase}, " +
            $"T case={tCase}, " +
            $"VBL={hasVbl}, " +
            $"RA2={hasRa2}, " +
            $"foot={footLog}.");
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
                facts,
                footOption);

        switch (footKind)
        {
            // C / C with CBR:
            // CL -> CL_MIN
            // CD -> CD_MIN
            case FootKind.C:
            case FootKind.CWithCbr:
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

            // VG:
            // GA -> GA_MIN
            // GD -> GD_MIN
            // B  -> B_MIN
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

            // G:
            // GO -> GO_MIN
            // GD -> GD_MIN
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

            // F has no special overlay tolerance override.
            case FootKind.F:
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
                facts,
                footOption);

        decimal footDepthMm;
        string source;

        switch (footKind)
        {
            // VG / G -> GD
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

            // C / C with CBR -> CD
            case FootKind.C:
            case FootKind.CWithCbr:
                footDepthMm =
                    RequireLength(
                        facts,
                        "CD",
                        footOption);

                source =
                    "CD";

                break;

            // F / unknown / anything else -> 0
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
            "[VMEquationPlanner] Foot depth -> " +
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
                "Cannot calculate VM foot_depth. " +
                $"{facts.EffectivePropertyName("Wed-Foot_Option")} '{DisplayToken(footOption)}' " +
                $"requires dimension '{dimensionKey}', " +
                "but it is missing or is not a millimeter dimension.");
        }

        if (valueMm < 0m)
        {
            throw new InvalidOperationException(
                "Cannot calculate VM foot_depth. " +
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
                $"[VMEquationPlanner] Missing/invalid bounds for '{dimensionKey}'. " +
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
                $"[VMEquationPlanner] Missing/invalid angle bounds for '{dimensionKey}'. " +
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

    private static TCase ResolveTCase(
        bool hasVbl,
        bool hasRa2)
    {
        if (!hasVbl && !hasRa2)
            return TCase.Case1;

        if (hasVbl && !hasRa2)
            return TCase.Case2;

        if (!hasVbl && hasRa2)
            return TCase.Case3;

        return TCase.Case4;
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
                "[VMEquationPlanner] VW is present but W is missing/not a length. " +
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
        WedgeFacts facts,
        string footOption)
    {
        switch (footOption)
        {
            case "LW_C":
            case "SW_C":
            case "C":
                return HasAllPositive(
                    facts,
                    "CBRL",
                    "CBRD")
                        ? FootKind.CWithCbr
                        : FootKind.C;

            case "LW_VG":
            case "SW_VG":
            case "VG":
                return FootKind.Vg;

            case "LW_G":
            case "SW_G":
            case "G":
                return FootKind.G;

            case "LW_F":
            case "SW_F":
            case "F":
                return FootKind.F;

            default:
                return FootKind.Unknown;
        }
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
        CWithCbr,
        Vg,
        G,
        F
    }

    private enum TCase
    {
        Case1,
        Case2,
        Case3,
        Case4
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}

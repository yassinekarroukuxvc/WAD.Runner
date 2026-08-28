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
/// Builds the equations used by the 4516 model.
///
/// 4516-specific rules:
///
/// Feed-hole:
///     STD / STD(Round) -> keep H
///     Oval             -> H = HH
///     Slot             -> H = ST
///
/// Foot depth:
///     VG               -> foot_depth = GD
///     G                -> foot_depth = GD
///     C                 -> foot_depth = CD
///     C with CBR       -> foot_depth = CD
///                         detected when Wed-Foot_Option is C
///                         and CBRL > 0 and CBRD > 0
///     Other            -> foot_depth = 0
///
/// SLB:
///     VBL > 0          -> BA = 0 degrees
///
/// Overlay common overrides:
///     W   = W_MAX
///     FL  = FL_MAX
///     T   = T_MAX
///     C   = C_MIN
///
/// Overlay VR/VW overrides, when VR > 0 and VW > 0:
///     VW  = VW_MAX
///     VR  = VR_MIN
///     VRA = VRA_MAX
///
/// Overlay Case 2, when VW > W:
///     ISA = ISA_MAX
///
/// FG overlay foot overrides:
///     VG         -> B_MIN, GD_MIN, GA_MIN
///     C          -> CL_MIN, CD_MIN
///     C with CBR -> CL_MIN, CD_MIN
///     G          -> GD_MIN, GO_MIN
/// </summary>
public sealed class _4516EquationPlanner : StandardEquationPlanner
{
    private const string FeedHoleHeightEquationName =
        "H";

    private const string OvalFeedHoleHeightDimension =
        "HH";

    private const string SlotFeedHoleHeightDimension =
        "ST";

    private const string FootDepthEquationName =
        "foot_depth";

    private const string FunnelGapEquationName =
        "funnel_gap";

    private const string BackAngleEquationName =
        "BA";

    private const string SlbDimensionName =
        "VBL";

    private const string NonStdCutEquationName =
        "non_std_cut";

    private readonly WedgeType _wedgeType;

    public _4516EquationPlanner(
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
                "WedgeData is required to build 4516 equations.");

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

        ApplyFeedHoleEquationRules(
            builder,
            facts);

        AddFootDepthEquation(
            builder,
            facts);

        AddFunnelGapEquation(
            builder,
            facts);

        // i was wrong we should not do this
        /*ApplySlbBackAngleRule(
            builder,
            facts);*/

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
        /*
         * These four overrides apply to both PGB and FG overlays.
         */
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
            dimensionKey: "FL",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "T",
            useMaximum: true);

        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "C",
            useMaximum: false);

        /*
         * The VR overlay family is active only when both VR and VW
         * have positive nominal values.
         */
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

        /*
         * PGB has no foot-option overrides.
         */
        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOverlayOverrides(
                builder,
                facts);
        }

        var normalizedFootOption =
            ResolveNormalizedFootOption(
                facts);

        Logger.Info(
            "[_4516EquationPlanner] Overlay dimension overrides -> " +
            $"subclass={subclass}, " +
            $"VR/VW present={hasVrVw}, " +
            $"VW case={overlayVwCase}, " +
            $"foot option={ResolveFootKind(facts, normalizedFootOption)}.");
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

        /*
         * Unlike CKVD, 4516 uses VR_MIN.
         */
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
            case FootKind.Vg:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "B",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GD",
                    useMaximum: false);

                AddAngleBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GA",
                    useMaximum: false);

                break;

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

            case FootKind.G:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GD",
                    useMaximum: false);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GO",
                    useMaximum: false);

                break;

            /*
             * No additional overlay dimension overrides were
             * specified for CC or flat feet.
             */
            case FootKind.CC:
            case FootKind.FlatOrUnknown:
            default:
                break;
        }

        Logger.Info(
            "[_4516EquationPlanner] FG overlay foot overrides -> " +
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
                "[_4516EquationPlanner] Missing or invalid nominal/" +
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
            "[_4516EquationPlanner] Overlay length bound -> " +
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
                "[_4516EquationPlanner] Missing or invalid nominal/" +
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
            "[_4516EquationPlanner] Overlay angle bound -> " +
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
            vwMillimeters <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[_4516EquationPlanner] VW is present but W is " +
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

        if (vwMillimeters >
            wMillimeters +
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case2;
        }

        Logger.Warn(
            "[_4516EquationPlanner] 4516 overlay received VW < W " +
            $"(VW={vwMillimeters} mm, W={wMillimeters} mm). " +
            "Only VW = W for Case 1 and VW > W for Case 2 are " +
            "currently defined.");

        return OverlayVwCase.None;
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
    // FEED-HOLE EQUATIONS
    // ================================================================

    private static void ApplyFeedHoleEquationRules(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var feedHoleToken =
            ResolveNormalizedFeedHoleType(
                facts);

        var feedHoleType =
            ResolveFeedHoleType(
                feedHoleToken);

        switch (feedHoleType)
        {
            case FeedHoleType.Std:
                Logger.Info(
                    "[_4516EquationPlanner] Feed-hole type STD -> " +
                    "the original H database equation remains active.");

                return;

            case FeedHoleType.Oval:
                AddFeedHoleHeightOverride(
                    builder,
                    facts,
                    sourceDimension: OvalFeedHoleHeightDimension,
                    feedHoleType: "Oval");

                return;

            case FeedHoleType.Slot:
                AddFeedHoleHeightOverride(
                    builder,
                    facts,
                    sourceDimension: SlotFeedHoleHeightDimension,
                    feedHoleType: "Slot");

                return;

            default:
                throw new InvalidOperationException(
                    "Cannot resolve the 4516 feed-hole type from " +
                    "'Wed-Feed_H/Slot'. Expected STD(Round), STD, " +
                    $"Oval or Slot, but received " +
                    $"'{DisplayToken(feedHoleToken)}'. The 4516 " +
                    "property validation must run before building " +
                    "the model equations.");
        }
    }

    private static void AddFeedHoleHeightOverride(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string sourceDimension,
        string feedHoleType)
    {
        if (!facts.TryGetLengthMm(
                sourceDimension,
                out var sourceValueMm))
        {
            throw new InvalidOperationException(
                $"Cannot apply the 4516 {feedHoleType} feed-hole rule. " +
                $"Dimension '{sourceDimension}' is required because " +
                $"H must be replaced with {sourceDimension}, but " +
                $"'{sourceDimension}' is missing or is not a " +
                "millimeter dimension.");
        }

        if (sourceValueMm <= 0m)
        {
            throw new InvalidOperationException(
                $"Cannot apply the 4516 {feedHoleType} feed-hole rule. " +
                $"Dimension '{sourceDimension}' must be greater than " +
                $"zero, but its value is {sourceValueMm} mm.");
        }

        builder.AddManaged(
            FeedHoleHeightEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FeedHoleHeightEquationName,
                sourceValueMm));

        Logger.Info(
            "[_4516EquationPlanner] Feed-hole equation override -> " +
            $"type={feedHoleType}, " +
            $"H={sourceDimension}={sourceValueMm} mm.");
    }

    private static string ResolveNormalizedFeedHoleType(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Feed_H/Slot",
                "Wed_Feed_H_Slot",
                "Wed Feed H Slot",
                "Wed-Feed H Slot",
                "Feed_H/Slot",
                "Feed_H_Slot",
                "Feed H Slot",
                "feed_h_slot");

        return NormalizeFeedHoleToken(
            raw);
    }

    private static string NormalizeFeedHoleToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token = raw
            .Trim()
            .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        if (separatorIndex >= 0)
        {
            token =
                token[..separatorIndex];
        }

        token = token
            .Trim()
            .ToUpperInvariant();

        if (token.StartsWith(
                "STD",
                StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith(
                "STANDARD",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STD";
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return "OVAL";
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return "SLOT";
        }

        return token;
    }

    private static FeedHoleType ResolveFeedHoleType(
        string normalizedToken)
    {
        return normalizedToken switch
        {
            "STD" =>
                FeedHoleType.Std,

            "OVAL" =>
                FeedHoleType.Oval,

            "SLOT" =>
                FeedHoleType.Slot,

            _ =>
                FeedHoleType.Unknown
        };
    }

    // ================================================================
    // FOOT-DEPTH EQUATION
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
            case FootKind.CC:
            case FootKind.CWithCbr:
                footDepthMm =
                    RequireFootDepthSource(
                        facts,
                        sourceDimension: "CD",
                        footOption);

                sourceDescription =
                    "CD";

                break;

            case FootKind.FlatOrUnknown:
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
            "[_4516EquationPlanner] Foot depth resolved -> " +
            $"Wed-Foot_Option='{DisplayToken(footOption)}', " +
            $"foot kind={footKind}, " +
            $"source={sourceDescription}, " +
            $"foot_depth={footDepthMm} mm.");
    }

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
        switch (normalizedFootOption)
        {
            case "LW_VG":
            case "SW_VG":
                return FootKind.Vg;

            case "LW_G":
            case "SW_G":
                return FootKind.G;

            case "LW_C":
            case "SW_C":
                return HasAllPositiveNominal(
                    facts,
                    "CBRL",
                    "CBRD")
                        ? FootKind.CWithCbr
                        : FootKind.C;

            case "LW_CC":
            case "SW_CC":
                return FootKind.CC;

            default:
                return FootKind.FlatOrUnknown;
        }
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
                "Cannot calculate 4516 foot_depth. " +
                $"Wed-Foot_Option '{DisplayToken(footOption)}' " +
                $"requires dimension '{sourceDimension}', " +
                "but that dimension is missing or is not a " +
                "millimeter dimension.");
        }

        if (valueMm < 0m)
        {
            throw new InvalidOperationException(
                "Cannot calculate 4516 foot_depth. " +
                $"Dimension '{sourceDimension}' has an invalid " +
                $"negative value: {valueMm} mm.");
        }

        return valueMm;
    }

    // ================================================================
    // FUNNEL-GAP EQUATION
    // ================================================================

    private static void AddFunnelGapEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        var funnelGapMm =
            EquationGeometry.FunnelGapMmOrDefault(
                facts);

        builder.AddManaged(
            FunnelGapEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FunnelGapEquationName,
                funnelGapMm));

        Logger.Info(
            "[_4516EquationPlanner] Funnel gap resolved -> " +
            $"funnel_gap={funnelGapMm} mm.");
    }

    // ================================================================
    // SLB / BA EQUATION
    // ================================================================

    private static void ApplySlbBackAngleRule(
        EquationPlanBuilder builder,
        WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm(
                SlbDimensionName,
                out var vblMm) ||
            vblMm <= WedgeFacts.DefaultPositiveEpsilon)
        {
            Logger.Info(
                "[_4516EquationPlanner] VBL is missing or zero. " +
                "The database BA value remains active.");

            return;
        }

        builder.AddManaged(
            BackAngleEquationName,
            EquationFormatting.Line(
                BackAngleEquationName,
                0m,
                "deg"));

        Logger.Info(
            "[_4516EquationPlanner] SLB rule applied -> " +
            $"VBL={vblMm} mm, BA=0 deg.");
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

        var token = raw
            .Trim()
            .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        if (separatorIndex >= 0)
        {
            token =
                token[..separatorIndex];
        }

        token = token
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .Trim('_')
            .ToUpperInvariant();

        while (token.Contains(
                   "__",
                   StringComparison.Ordinal))
        {
            token = token.Replace(
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

    private enum FeedHoleType
    {
        Unknown,
        Std,
        Oval,
        Slot
    }

    private enum FootKind
    {
        FlatOrUnknown,
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
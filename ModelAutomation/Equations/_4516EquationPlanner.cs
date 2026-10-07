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
///     VG               -> foot_depth = effective GD
///     G                -> foot_depth = effective GD
///     C                 -> foot_depth = effective CD
///     C with CBR       -> foot_depth = effective CD
///                         detected when the subclass foot option (Wed-Foot_Option; FG only) is C
///                         and CBRL > 0 and CBRD > 0
///     CG / CC / F      -> foot_depth = 0
///
/// 4516 foot profile (FG):
///     F is read from the DB only and is NOT sent to SolidWorks.
///     Other inputs use the effective equation value after overrides.
///     Example: overlay FL_MAX / GD_MIN / CD_MIN are used when active.
///     split    = (FL - F) / 2
///     BR limit = BR * tan(45 - FTA/2) - foot_depth * tan(FTA)
///     FRX      = min(split, FR)
///     BRX      = min(split, BR limit)
///     flat     = FL - FRX - BRX
///
///     Reject when F >= FL, BR limit <= 0, FRX <= 0, BRX <= 0,
///     flat <= 0, or F is missing/invalid.
///
/// Overlay overrides:
///     PGB: W = W_MAX; T = T_MAX; when C > 0 also FL = FL_MAX and C = C_MIN
///     FG : FL = FL_MAX; T = T_MAX; C = C_MIN
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
///
/// Special values:
///     missing FTA -> 0 deg
///     VRA = 90    -> 0 deg
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

    private const string FrontProfileXEquationName =
        "FRX";

    private const string BackProfileXEquationName =
        "BRX";

    public _4516EquationPlanner(
        WedgeType wedgeType)
    {
        // The registry constructs this planner with WedgeType._4516.
        // Keep the constructor signature stable for the existing registry.
        _ = wedgeType;
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
            ?? new WedgeFacts(wedge, context.Subclass);

        /*
         * Tracks the values that the equation updater will actually
         * send to SolidWorks.
         *
         * If an overlay rule replaces FL with FL_MAX, GD with GD_MIN,
         * CD with CD_MIN, or any other tracked formula input in the
         * future, the 4516 foot-profile calculation reads that updated
         * value instead of going back to the nominal DB value.
         */
        var effectiveValues =
            new EffectiveEquationValues(facts);

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        /*
         * 4516 SolidWorks contract:
         *
         * F remains a DB input, but it is now a driven/reference
         * dimension in the SolidWorks sketch. Do not write it to
         * the equation file.
         *
         * FRX and BRX are calculated by the program for 4516 FG,
         * so ignore any DB values for those keys as well.
         */
        dimensions.Remove(DimensionKey.From("F"));
        dimensions.Remove(DimensionKey.From("FRX"));
        dimensions.Remove(DimensionKey.From("BRX"));

        var builder = new EquationPlanBuilder()
            .WithDimensions(
                dimensions,
                EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions()
            .ZeroMissingKeys(
                new[] { "FTA" },
                new[] { "FTA" });

        builder.AddManaged(
            "TL",
            EquationFormatting.LengthLineFromMillimeters(
                "TL",
                18.0));

        if (context.Subclass == WedgeSubclass.FG)
        {
            ApplyFeedHoleEquationRules(
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
                context.Subclass,
                effectiveValues: effectiveValues);
        }

        ApplySpecialDimensionOverrides(
            builder,
            facts,
            effectiveValues: effectiveValues);

        /*
         * IMPORTANT ORDER:
         *
         * The foot depth and FRX/BRX calculation must happen AFTER
         * the equation overrides above. That way the calculation uses
         * the exact values selected for the model, for example:
         *
         *   FL -> FL_MAX
         *   GD -> GD_MIN
         *   CD -> CD_MIN
         *
         * When no override was applied, EffectiveEquationValues falls
         * back to the nominal DB value.
         */
        if (context.Subclass == WedgeSubclass.FG)
        {
            AddFootDepthEquation(
                builder,
                facts,
                effectiveValues: effectiveValues);

            AddFootProfileEquations(
                builder,
                facts,
                effectiveValues: effectiveValues);
        }

        AddEngravingStart(
            builder,
            context);

        AddOverlayScale(
            builder,
            context);

        return builder.Build();
    }

    // ================================================================
    // OVERLAY DIMENSION OVERRIDES
    // ================================================================

    private static void AddOverlayDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass,
        EffectiveEquationValues effectiveValues)
    {
        var hasPositiveC =
            facts.HasPositive("C");

        if (subclass == WedgeSubclass.PGB)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "W",
                useMaximum: true,
                effectiveValues: effectiveValues);

            if (hasPositiveC)
            {
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "FL",
                    useMaximum: true,
                    effectiveValues: effectiveValues);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "C",
                    useMaximum: false,
                    effectiveValues: effectiveValues);
            }
        }
        else if (subclass == WedgeSubclass.FG)
        {
            AddLengthBoundEquation(
                builder,
                facts,
                dimensionKey: "FL",
                useMaximum: true,
                effectiveValues: effectiveValues);

            AddLengthBoundEquation(
                builder,
                facts,
                dimensionKey: "C",
                useMaximum: false,
                effectiveValues: effectiveValues);
        }

        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "T",
            useMaximum: true,
            effectiveValues: effectiveValues);

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
                facts,
                effectiveValues: effectiveValues);

            if (overlayVwCase == OverlayVwCase.Case2)
            {
                AddAngleBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "ISA",
                    useMaximum: true,
                    effectiveValues: effectiveValues);
            }
        }

        /*
         * PGB has no foot-option overrides.
         */
        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOverlayOverrides(
                builder,
                facts,
                effectiveValues: effectiveValues);
        }

        var footLog =
            subclass == WedgeSubclass.FG
                ? ResolveFootKind(
                    facts,
                    ResolveNormalizedFootOption(facts)).ToString()
                : "N/A";

        Logger.Info(
            "[_4516EquationPlanner] Overlay dimension overrides -> " +
            $"subclass={subclass}, " +
            $"C>0={hasPositiveC}, " +
            $"VR/VW present={hasVrVw}, " +
            $"VW case={overlayVwCase}, " +
            $"foot option={footLog}.");
    }

    private static void AddVrVwOverlayOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues)
    {
        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "VW",
            useMaximum: true,
            effectiveValues: effectiveValues);

        /*
         * Unlike CKVD, 4516 uses VR_MIN.
         */
        AddLengthBoundEquation(
            builder,
            facts,
            dimensionKey: "VR",
            useMaximum: false,
            effectiveValues: effectiveValues);

        AddAngleBoundEquation(
            builder,
            facts,
            dimensionKey: "VRA",
            useMaximum: true,
            effectiveValues: effectiveValues);
    }

    private static void AddFgFootOverlayOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues)
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
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GD",
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                AddAngleBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GA",
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                break;

            case FootKind.C:
            case FootKind.CWithCbr:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "CL",
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "CD",
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                break;

            case FootKind.G:
                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GD",
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                AddLengthBoundEquation(
                    builder,
                    facts,
                    dimensionKey: "GO",
                    useMaximum: false,
                    effectiveValues: effectiveValues);

                break;

            /*
             * No additional overlay dimension overrides were
             * specified for CG, CC or F.
             */
            case FootKind.CG:
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

    private static void ApplySpecialDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues)
    {
        // 4516 database convention: VRA=90 means the model equation must use 0 deg.
        if (facts.TryGetAngleDeg(
                "VRA",
                out var vraDegrees) &&
            decimal.Abs(vraDegrees - 90m) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            builder.AddManaged(
                "VRA",
                EquationFormatting.Line(
                    "VRA",
                    0m,
                    "deg"));

            effectiveValues.SetAngleDeg(
                "VRA",
                0m,
                "special VRA=90 -> 0 override");

            Logger.Info(
                "[_4516EquationPlanner] VRA database value is 90 deg -> model VRA set to 0 deg.");
        }
    }

    private static void AddLengthBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum,
        EffectiveEquationValues? effectiveValues = null)
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

        effectiveValues?.SetLengthMm(
            dimensionKey,
            selectedMillimeters,
            useMaximum ? "MAX overlay override" : "MIN overlay override");

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
        bool useMaximum,
        EffectiveEquationValues? effectiveValues = null)
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

        effectiveValues?.SetAngleDeg(
            dimensionKey,
            selectedDegrees,
            useMaximum ? "MAX overlay override" : "MIN overlay override");

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
                    $"'{facts.EffectivePropertyName("Wed-Feed_H/Slot")}'. Expected STD(Round), STD, " +
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
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues)
    {
        var footOption =
            ResolveNormalizedFootOption(
                facts);

        var footKind =
            ResolveFootKind(
                facts,
                footOption);

        var footDepthMm =
            ResolveEffectiveFootDepthMm(
                facts,
                effectiveValues,
                footKind,
                footOption,
                out var sourceDescription);

        builder.AddManaged(
            FootDepthEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FootDepthEquationName,
                footDepthMm));

        Logger.Info(
            "[_4516EquationPlanner] Foot depth resolved -> " +
            $"{facts.EffectivePropertyName("Wed-Foot_Option")}='{DisplayToken(footOption)}', " +
            $"foot kind={footKind}, " +
            $"source={sourceDescription}, " +
            $"foot_depth={footDepthMm} mm.");
    }

    private static decimal ResolveEffectiveFootDepthMm(
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues,
        FootKind footKind,
        string footOption,
        out string sourceDescription)
    {
        switch (footKind)
        {
            case FootKind.Vg:
            case FootKind.G:
                return RequireEffectiveFootDepthSource(
                    facts,
                    effectiveValues,
                    sourceDimension: "GD",
                    footOption,
                    out sourceDescription);

            case FootKind.C:
            case FootKind.CWithCbr:
                return RequireEffectiveFootDepthSource(
                    facts,
                    effectiveValues,
                    sourceDimension: "CD",
                    footOption,
                    out sourceDescription);

            case FootKind.CG:
            case FootKind.CC:
            case FootKind.FlatOrUnknown:
            default:
                sourceDescription =
                    "0 (foot option does not use GD/CD)";

                return 0m;
        }
    }

    private static decimal RequireEffectiveFootDepthSource(
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues,
        string sourceDimension,
        string footOption,
        out string sourceDescription)
    {
        if (!effectiveValues.TryGetLengthMm(
                sourceDimension,
                out var valueMm,
                out var source))
        {
            throw new InvalidOperationException(
                "Cannot calculate 4516 foot_depth. " +
                $"{facts.EffectivePropertyName("Wed-Foot_Option")} '{DisplayToken(footOption)}' " +
                $"requires dimension '{sourceDimension}', " +
                "but that dimension is missing or is not a " +
                "millimeter dimension.");
        }

        if (valueMm < 0m)
        {
            throw new InvalidOperationException(
                "Cannot calculate 4516 foot_depth. " +
                $"Effective dimension '{sourceDimension}' has an invalid " +
                $"negative value: {valueMm} mm.");
        }

        sourceDescription =
            $"{sourceDimension} ({source})";

        return valueMm;
    }

    // ================================================================
    // 4516 FOOT PROFILE (FRX / BRX)
    // ================================================================

    private static void AddFootProfileEquations(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        EffectiveEquationValues effectiveValues)
    {
        var flMm =
            RequireEffectiveFootProfileLength(
                effectiveValues,
                "FL",
                out var flSource);

        /*
         * F is intentionally different from the other inputs:
         * it is always the DB/reference value and is NEVER sent to
         * SolidWorks. Therefore an equation override cannot replace F.
         */
        if (!facts.TryGetLengthMm(
                "F",
                out var fMm))
        {
            throw RejectFootProfile(
                "F is missing or invalid in the database. " +
                "The 4516 tool must be skipped until SPT confirms the rule.");
        }

        var frMm =
            RequireEffectiveFootProfileLength(
                effectiveValues,
                "FR",
                out var frSource);

        var brMm =
            RequireEffectiveFootProfileLength(
                effectiveValues,
                "BR",
                out var brSource);

        /*
         * Preserve the existing 4516 convention:
         * missing FTA -> 0 degrees.
         *
         * If a future equation rule overrides FTA before this point,
         * the effective angle is used automatically.
         */
        decimal ftaDeg;
        string ftaSource;

        if (!effectiveValues.TryGetAngleDeg(
                "FTA",
                out ftaDeg,
                out ftaSource))
        {
            ftaDeg = 0m;
            ftaSource = "missing -> 0 deg";
        }

        var footOption =
            ResolveNormalizedFootOption(
                facts);

        var footKind =
            ResolveFootKind(
                facts,
                footOption);

        var footDepthMm =
            ResolveEffectiveFootDepthMm(
                facts,
                effectiveValues,
                footKind,
                footOption,
                out var footDepthSource);

        if (!EquationGeometry.TryCalculate4516FootProfile(
                flMm,
                fMm,
                footDepthMm,
                frMm,
                brMm,
                ftaDeg,
                out var frxMm,
                out var brxMm,
                out var flatMm,
                out var splitMm,
                out var brLimitMm,
                out var error))
        {
            throw RejectFootProfile(
                error);
        }

        builder.AddManaged(
            FrontProfileXEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FrontProfileXEquationName,
                frxMm));

        builder.AddManaged(
            BackProfileXEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                BackProfileXEquationName,
                brxMm));

        Logger.Info(
            "[_4516EquationPlanner] Foot profile calculated from EFFECTIVE equation values -> " +
            $"FL={flMm} mm ({flSource}), " +
            $"F(DB/reference only)={fMm} mm, " +
            $"foot_depth={footDepthMm} mm ({footDepthSource}), " +
            $"FR={frMm} mm ({frSource}), " +
            $"BR={brMm} mm ({brSource}), " +
            $"FTA={ftaDeg} deg ({ftaSource}), " +
            $"Split={splitMm} mm, " +
            $"BR limit={brLimitMm} mm, " +
            $"FRX={frxMm} mm, " +
            $"BRX={brxMm} mm, " +
            $"Flat={flatMm} mm. " +
            "F was not sent to SolidWorks.");
    }

    private static decimal RequireEffectiveFootProfileLength(
        EffectiveEquationValues effectiveValues,
        string dimensionKey,
        out string source)
    {
        if (!effectiveValues.TryGetLengthMm(
                dimensionKey,
                out var valueMm,
                out source))
        {
            throw RejectFootProfile(
                $"Dimension '{dimensionKey}' is missing or is not a millimeter dimension.");
        }

        return valueMm;
    }

    private static InvalidOperationException RejectFootProfile(
        string reason)
    {
        Logger.Warn(
            "[_4516EquationPlanner] 4516 foot profile rejected -> " +
            reason);

        return new InvalidOperationException(
            "Cannot generate 4516 tool. " +
            reason);
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
        return normalizedFootOption switch
        {
            "LW_VG" or "SW_VG" or "VG" =>
                FootKind.Vg,

            "LW_G" or "SW_G" or "G" =>
                FootKind.G,

            "LW_C" or "SW_C" or "C" =>
                HasAllPositiveNominal(
                    facts,
                    "CBRL",
                    "CBRD")
                        ? FootKind.CWithCbr
                        : FootKind.C,

            "LW_CG" or "SW_CG" or "CG" =>
                FootKind.CG,

            "LW_CC" or "SW_CC" or "CC" =>
                FootKind.CC,

            "LW_F" or "SW_F" or "F" =>
                FootKind.FlatOrUnknown,

            _ =>
                FootKind.FlatOrUnknown
        };
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

    /// <summary>
    /// Keeps the effective equation values selected during this planner
    /// build. Overrides are stored here as soon as the corresponding
    /// equation is changed; reads fall back to the nominal DB value.
    ///
    /// This is intentionally local to the 4516 planner. EquationPlanBuilder
    /// remains generic and unchanged.
    /// </summary>
    private sealed class EffectiveEquationValues
    {
        private readonly WedgeFacts _facts;

        private readonly Dictionary<string, EffectiveDecimalValue>
            _lengthOverrides =
                new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, EffectiveDecimalValue>
            _angleOverrides =
                new(StringComparer.OrdinalIgnoreCase);

        public EffectiveEquationValues(
            WedgeFacts facts)
        {
            _facts = facts ??
                throw new ArgumentNullException(nameof(facts));
        }

        public void SetLengthMm(
            string key,
            decimal valueMm,
            string source)
        {
            _lengthOverrides[key] =
                new EffectiveDecimalValue(
                    valueMm,
                    source);
        }

        public void SetAngleDeg(
            string key,
            decimal valueDeg,
            string source)
        {
            _angleOverrides[key] =
                new EffectiveDecimalValue(
                    valueDeg,
                    source);
        }

        public bool TryGetLengthMm(
            string key,
            out decimal valueMm,
            out string source)
        {
            if (_lengthOverrides.TryGetValue(
                    key,
                    out var overridden))
            {
                valueMm = overridden.Value;
                source = overridden.Source;
                return true;
            }

            if (_facts.TryGetLengthMm(
                    key,
                    out valueMm))
            {
                source = "nominal DB value";
                return true;
            }

            valueMm = 0m;
            source = "missing";
            return false;
        }

        public bool TryGetAngleDeg(
            string key,
            out decimal valueDeg,
            out string source)
        {
            if (_angleOverrides.TryGetValue(
                    key,
                    out var overridden))
            {
                valueDeg = overridden.Value;
                source = overridden.Source;
                return true;
            }

            if (_facts.TryGetAngleDeg(
                    key,
                    out valueDeg))
            {
                source = "nominal DB value";
                return true;
            }

            valueDeg = 0m;
            source = "missing";
            return false;
        }
    }

    private readonly record struct EffectiveDecimalValue(
        decimal Value,
        string Source);

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
        CG,
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
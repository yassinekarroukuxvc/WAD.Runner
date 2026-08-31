using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

/// <summary>
/// Builds the equations used by the 45CK model.
///
/// Feed hole:
///     STD  -> keep H
///     Oval -> H = HH
///     Slot -> H = ST
///
/// Foot depth:
///     LW_VG -> foot_depth = GD
///     other -> foot_depth = 0
///
/// Funnel gap:
///     EquationGeometry.FunnelGapMmOrDefault(facts)
///
/// Overlay PGB:
///     W = W_MAX
///     T = T_MAX
///     FD = FD_MAX
///     RA2H = RA2H_MAX when RA2H > 0
///     VW = VW_MAX, VR = VR_MIN, VRA = VRA_MAX when VR/VW are present
///     ISA = ISA_MAX for VR/VW Case 2 (VW != W)
///
/// Overlay FG:
///     W remains at its nominal value
///     T = T_MAX
///     FD = FD_MAX
///     RA2H = RA2H_MAX when RA2H > 0
///     VW = VW_MAX, VR = VR_MIN, VRA = VRA_MAX when VR/VW are present
///     ISA = ISA_MAX for VR/VW Case 2 (VW != W)
///
/// FG VG overlay only:
///     GA = GA_MIN
///     GD = GD_MIN
///     B = B_MIN
/// </summary>
public sealed class _45CKEquationPlanner : StandardEquationPlanner
{
    private const string FootDepthEquationName = "foot_depth";

    public override EquationPlan Build(
        ModelAutomationContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var wedge = context.Wedge
            ?? throw new InvalidOperationException(
                "WedgeData is required to build 45CK equations.");

        var facts =
            context.Facts ??
            new WedgeFacts(wedge);

        var dimensions =
            new Dictionary<DimensionKey, DomDim>(
                wedge.Dimensions);

        if (context.Subclass == WedgeSubclass.FG)
            ApplyFeedHoleHValue(facts, dimensions);

        var funnelGap =
            EquationGeometry.FunnelGapMmOrDefault(
                facts);

        UpsertLengthMm(
            dimensions,
            EquationCatalog.Names.FunnelGap,
            funnelGap);

        var builder =
            new EquationPlanBuilder()
                .WithDimensions(
                    dimensions,
                    EquationCatalog.DbToModelAliases)
                .SkipProvidedZeroDimensions();

        builder.AddManaged(
            "TL",
            EquationFormatting.LengthLineFromMillimeters(
                "TL",
                20.0));

        AddFootDepthEquation(
            builder,
            facts,
            context.Subclass);

        builder.AddManaged(
            EquationCatalog.Names.FunnelGap,
            EquationFormatting.LengthLineFromMillimeters(
                EquationCatalog.Names.FunnelGap,
                funnelGap));

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

        Logger.Info(
            "[_45CKEquationPlanner] Build -> " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"funnel_gap={funnelGap} mm.");

        return builder.Build();
    }

    // ================================================================
    // FEED HOLE
    // ================================================================

    private static void ApplyFeedHoleHValue(
        WedgeFacts facts,
        IDictionary<DimensionKey, DomDim> dimensions)
    {
        var token =
            ResolveNormalizedFeedHoleType(
                facts);

        switch (token)
        {
            case "STD":
                Logger.Info(
                    "[_45CKEquationPlanner] Feed-hole type STD -> " +
                    "original H remains active.");

                return;

            case "OVAL":
                UpsertHFromSource(
                    facts,
                    dimensions,
                    "HH",
                    "Oval");

                return;

            case "SLOT":
                UpsertHFromSource(
                    facts,
                    dimensions,
                    "ST",
                    "Slot");

                return;

            default:
                throw new InvalidOperationException(
                    "Cannot resolve the 45CK feed-hole type from " +
                    "'Wed-Feed_H/Slot'. Expected STD, Oval or Slot, " +
                    $"but received '{DisplayToken(token)}'.");
        }
    }

    private static void UpsertHFromSource(
        WedgeFacts facts,
        IDictionary<DimensionKey, DomDim> dimensions,
        string sourceDimension,
        string feedHoleType)
    {
        if (!facts.TryGetLengthMm(
                sourceDimension,
                out var sourceMm) ||
            sourceMm <= WedgeFacts.DefaultPositiveEpsilon)
        {
            throw new InvalidOperationException(
                $"Cannot apply the 45CK {feedHoleType} feed-hole equation. " +
                $"Dimension '{sourceDimension}' must be a positive " +
                $"millimeter value because H = {sourceDimension}.");
        }

        UpsertLengthMm(
            dimensions,
            "H",
            sourceMm);

        Logger.Info(
            "[_45CKEquationPlanner] Feed-hole equation -> " +
            $"type={feedHoleType}, " +
            $"H={sourceDimension}={sourceMm} mm.");
    }

    // ================================================================
    // FOOT DEPTH
    // ================================================================

    private static void AddFootDepthEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        var footOption =
            ResolveNormalizedFootOption(
                facts);

        var isVg =
            subclass == WedgeSubclass.FG &&
            (footOption == "LW_VG" ||
             footOption == "VG");

        decimal footDepthMm = 0m;

        if (isVg)
        {
            if (!facts.TryGetLengthMm(
                    "GD",
                    out footDepthMm) ||
                footDepthMm <= 0m)
            {
                throw new InvalidOperationException(
                    "Cannot calculate 45CK foot_depth. " +
                    "Foot option LW_VG requires dimension 'GD' " +
                    "to be a positive millimeter value.");
            }
        }

        builder.AddManaged(
            FootDepthEquationName,
            EquationFormatting.LengthLineFromMillimeters(
                FootDepthEquationName,
                footDepthMm));

        Logger.Info(
            "[_45CKEquationPlanner] Foot depth -> " +
            $"footOption='{DisplayToken(footOption)}', " +
            $"foot_depth={footDepthMm} mm.");
    }

    // ================================================================
    // OVERLAY DIMENSION OVERRIDES
    // ================================================================

    private static void AddOverlayDimensionOverrides(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        // PGB:
        // W uses W_MAX.
        //
        // FG:
        // W is intentionally NOT overridden.
        // The nominal W value already loaded from the database remains active.
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
            "FD",
            useMaximum: true);

        if (facts.HasPositive("RA2H"))
        {
            AddLengthBoundEquation(
                builder,
                facts,
                "RA2H",
                useMaximum: true);
        }

        var hasVrVw =
            facts.HasPositive("VR") &&
            facts.HasPositive("VW");

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

            if (vwCase == OverlayVwCase.Case2)
            {
                AddAngleBoundEquation(
                    builder,
                    facts,
                    "ISA",
                    useMaximum: true);
            }
        }

        if (subclass == WedgeSubclass.FG &&
            ResolveFootOption(facts) == FootOptionType.Vg)
        {
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
        }

        Logger.Info(
            "[_45CKEquationPlanner] Overlay overrides -> " +
            $"subclass={subclass}, " +
            $"W override=" +
            $"{(subclass == WedgeSubclass.PGB ? "MAX" : "NOMINAL")}, " +
            $"VR/VW={hasVrVw}, " +
            $"case={vwCase}, " +
            $"RA2H={facts.HasPositive("RA2H")}, " +
            $"foot={ResolveFootOption(facts)}.");
    }

    // ================================================================
    // LENGTH BOUND EQUATIONS
    // ================================================================

    private static void AddLengthBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minimumMm,
                out var maximumMm))
        {
            Logger.Warn(
                "[_45CKEquationPlanner] Missing/invalid nominal or " +
                $"tolerance for length dimension '{dimensionKey}'. " +
                $"{(useMaximum ? "MAX" : "MIN")} override skipped.");

            return;
        }

        var valueMm =
            useMaximum
                ? maximumMm
                : minimumMm;

        builder.AddManaged(
            dimensionKey,
            EquationFormatting.LengthLineFromMillimeters(
                dimensionKey,
                valueMm));
    }

    // ================================================================
    // ANGLE BOUND EQUATIONS
    // ================================================================

    private static void AddAngleBoundEquation(
        EquationPlanBuilder builder,
        WedgeFacts facts,
        string dimensionKey,
        bool useMaximum)
    {
        if (!facts.TryGetAngleBoundsDeg(
                dimensionKey,
                out var minimumDeg,
                out var maximumDeg))
        {
            Logger.Warn(
                "[_45CKEquationPlanner] Missing/invalid nominal or " +
                $"tolerance for angle dimension '{dimensionKey}'. " +
                $"{(useMaximum ? "MAX" : "MIN")} override skipped.");

            return;
        }

        var valueDeg =
            useMaximum
                ? maximumDeg
                : minimumDeg;

        builder.AddManaged(
            dimensionKey,
            EquationFormatting.Line(
                dimensionKey,
                valueDeg,
                "deg"));
    }

    // ================================================================
    // OVERLAY VW CASE
    // ================================================================

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vwMm) ||
            !facts.TryGetLengthMm(
                "W",
                out var wMm))
        {
            return OverlayVwCase.None;
        }

        return decimal.Abs(
                   vwMm -
                   wMm) <=
               WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVwCase.Case1
            : OverlayVwCase.Case2;
    }

    // ================================================================
    // FEED HOLE RESOLUTION
    // ================================================================

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

        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            RemovePackedDatabaseSuffix(raw)
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

    // ================================================================
    // FOOT OPTION RESOLUTION
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

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts)
    {
        return ResolveNormalizedFootOption(
            facts) switch
        {
            "LW_VG" or "VG" =>
                FootOptionType.Vg,

            "LW_CG" or "CG" =>
                FootOptionType.Cg,

            _ =>
                FootOptionType.Unknown
        };
    }

    // ================================================================
    // DIMENSION UPSERT
    // ================================================================

    private static void UpsertLengthMm(
        IDictionary<DimensionKey, DomDim> dimensions,
        string key,
        decimal mm)
    {
        var dimensionKey =
            DimensionKey.From(
                key);

        dimensions[dimensionKey] =
            DomDim.CreateLength(
                dimensionKey,
                Quantity.MmOf(mm),
                Tolerance.Zero,
                null);
    }

    // ================================================================
    // TOKEN HELPERS
    // ================================================================

    private static string NormalizePackedToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            RemovePackedDatabaseSuffix(raw)
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

    private static string RemovePackedDatabaseSuffix(
        string raw)
    {
        var token =
            raw
                .Trim()
                .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        return separatorIndex >= 0
            ? token[..separatorIndex]
            : token;
    }

    private static string DisplayToken(
        string? token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;
    }

    // ================================================================
    // ENUMS
    // ================================================================

    private enum FootOptionType
    {
        Unknown,
        Vg,
        Cg
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
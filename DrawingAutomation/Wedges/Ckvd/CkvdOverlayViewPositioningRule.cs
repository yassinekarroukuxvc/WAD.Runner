using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges.Ckvd;

public sealed class CkvdOverlayViewPositioningRule
    : OverlayViewPositioningRuleBase
{
    public override string Name =>
        "CKVD overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        var hasVr =
            context.HasPositiveLength("VR") ||
            context.HasPositiveLength("VRR");

        var hasVw =
            context.HasPositiveLength("VW");

        if (hasVr != hasVw)
        {
            Logger.Warn(
                "[Overlay][CKVD] Mixed VR-family/VW state detected. " +
                $"VR family present={hasVr}, VW present={hasVw}. " +
                "Detail will use the standard reference point.");
        }

        var detailReferencePoint =
            hasVr && hasVw
                ? OverlayReferencePointNames.CkvdNonStandardCut
                : OverlayReferencePointNames.CkvdStandard;

        var style =
            ResolveStyle(context);

        var sectionReferencePoint =
            style == CkvdStyle.StyleA
                ? OverlayReferencePointNames.CkvdStyleA
                : OverlayReferencePointNames.CkvdStyleB;

        Logger.Info(
            "[Overlay][CKVD] Reference-point selection -> " +
            $"Subclass={context.Subclass}, " +
            $"VRFamily={hasVr}, VW={hasVw}, style={style}, " +
            $"Detail='{detailReferencePoint}', " +
            $"Section='{sectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            detailReferencePoint,
            sectionReferencePoint,
            primaryReferencePoint:
                OverlayReferencePointNames.CkvdStandard);
    }

    // ================================================================
    // STYLE RESOLUTION
    // ================================================================

    private static CkvdStyle ResolveStyle(
        OverlayViewPositioningContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        return context.Subclass switch
        {
            WedgeSubclass.PGB =>
                ResolvePgbStyle(context),

            WedgeSubclass.FG =>
                ResolveFgStyle(context),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported CKVD subclass '{context.Subclass}' " +
                    "for overlay view positioning.")
        };
    }

    // ================================================================
    // PGB STYLE
    // ================================================================

    /// <summary>
    /// PGB CKVD style is resolved ONLY from PGB-Type.
    ///
    /// Expected values:
    ///     LW_STYLE_A_CKVD
    ///     LW_STYLE_B_CKVD
    ///
    /// There is deliberately no fallback to Wed-Type.
    /// </summary>
    private static CkvdStyle ResolvePgbStyle(
        OverlayViewPositioningContext context)
    {
        var token =
            context.NormalizedPropertyToken(
                "PGB-Type",
                "PGB_Type",
                "PGB Type");

        return ResolveStyleToken(
            token,
            propertyName: "PGB-Type");
    }

    // ================================================================
    // FG STYLE
    // ================================================================

    /// <summary>
    /// FG CKVD style is resolved ONLY from FG properties.
    ///
    /// Expected values:
    ///     LW_STYLE_A_CKVD
    ///     LW_STYLE_B_CKVD
    ///
    /// There is deliberately no fallback to PGB-Type.
    /// </summary>
    private static CkvdStyle ResolveFgStyle(
        OverlayViewPositioningContext context)
    {
        var token =
            context.NormalizedPropertyToken(
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Shank_Type",
                "shank_type");

        return ResolveStyleToken(
            token,
            propertyName: "Wed-Type");
    }

    // ================================================================
    // COMMON STYLE TOKEN
    // ================================================================

    private static CkvdStyle ResolveStyleToken(
        string token,
        string propertyName)
    {
        if (string.Equals(
                token,
                "LW_STYLE_A_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdStyle.StyleA;
        }

        if (string.Equals(
                token,
                "LW_STYLE_B_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdStyle.StyleB;
        }

        throw new InvalidOperationException(
            $"Unable to resolve the CKVD Section reference point from '{propertyName}'. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{Display(token)}'.");
    }

    private static string Display(
        string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<missing>"
            : value;

    private enum CkvdStyle
    {
        StyleA,
        StyleB
    }
}
using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges.Ckvd;

public sealed class CkvdOverlayViewPositioningRule
    : OverlayViewPositioningRuleBase
{
    public override string Name => "CKVD overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        var hasVr = context.HasPositiveLength("VR") ||
                    context.HasPositiveLength("VRR");
        var hasVw = context.HasPositiveLength("VW");

        if (hasVr != hasVw)
        {
            Logger.Warn(
                "[Overlay][CKVD] Mixed VR-family/VW state detected. " +
                $"VR family present={hasVr}, VW present={hasVw}. " +
                "Detail will use the standard reference point.");
        }

        var detailReferencePoint = hasVr && hasVw
            ? OverlayReferencePointNames.CkvdNonStandardCut
            : OverlayReferencePointNames.CkvdStandard;

        var style = ResolveStyle(context);
        var sectionReferencePoint = style == CkvdStyle.StyleA
            ? OverlayReferencePointNames.CkvdStyleA
            : OverlayReferencePointNames.CkvdStyleB;

        Logger.Info(
            "[Overlay][CKVD] Reference-point selection -> " +
            $"VRFamily={hasVr}, VW={hasVw}, style={style}, " +
            $"Detail='{detailReferencePoint}', Section='{sectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            detailReferencePoint,
            sectionReferencePoint,
            primaryReferencePoint: OverlayReferencePointNames.CkvdStandard);
    }

    private static CkvdStyle ResolveStyle(
        OverlayViewPositioningContext context)
    {
        var wedType = context.NormalizedPropertyToken(
            "Wed-Type",
            "Wed_Type",
            "Wed Type",
            "Shank_Type",
            "shank_type");

        if (string.Equals(wedType, "LW_STYLE_A_CKVD", StringComparison.OrdinalIgnoreCase))
            return CkvdStyle.StyleA;

        if (string.Equals(wedType, "LW_STYLE_B_CKVD", StringComparison.OrdinalIgnoreCase))
            return CkvdStyle.StyleB;

        throw new InvalidOperationException(
            "Unable to resolve the CKVD Section reference point from Wed-Type. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{wedType}'.");
    }

    private enum CkvdStyle
    {
        StyleA,
        StyleB
    }
}

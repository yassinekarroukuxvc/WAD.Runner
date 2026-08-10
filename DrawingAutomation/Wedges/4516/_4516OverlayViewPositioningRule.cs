using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges._4516;

public sealed class _4516OverlayViewPositioningRule
    : OverlayViewPositioningRuleBase
{
    private const string StandardDetailReferencePoint =
        "ref_point_1";

    private const string SectionReferencePoint =
        "ref_point_2";

    private const string NonStandardCutReferencePoint =
        "ref_point_non_std_cut";

    public override string Name =>
        "4516 overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        /*
         * The VR family follows the same logic as CKVD:
         *
         * - VR > 0, or
         * - VRR > 0
         */
        var hasVr =
            context.HasPositiveLength("VR") ||
            context.HasPositiveLength("VRR");

        var hasVw =
            context.HasPositiveLength("VW");

        if (hasVr != hasVw)
        {
            Logger.Warn(
                "[Overlay][4516] Mixed VR-family/VW state detected. " +
                $"VR family present={hasVr}, VW present={hasVw}. " +
                "Detail will use ref_point_1.");
        }

        /*
         * Standard case:
         *
         * Detail  -> ref_point_1
         * Section -> ref_point_2
         *
         * VR/VW case:
         *
         * Detail  -> ref_point_non_std_cut
         * Section -> ref_point_2
         */
        var detailReferencePoint =
            hasVr && hasVw
                ? NonStandardCutReferencePoint
                : StandardDetailReferencePoint;

        Logger.Info(
            "[Overlay][4516] Reference-point selection -> " +
            $"VRFamily={hasVr}, VW={hasVw}, " +
            $"Detail='{detailReferencePoint}', " +
            $"Section='{SectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            detailReferencePoint,
            SectionReferencePoint,
            primaryReferencePoint:
                StandardDetailReferencePoint);
    }
}
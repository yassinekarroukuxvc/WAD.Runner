using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges.M;

public sealed class MOverlayViewPositioningRule : OverlayViewPositioningRuleBase
{
    private const string StdDetailReferencePoint = "std_ref_point_right";
    private const string StdSectionReferencePoint = "std_ref_point_left";
    private const string RevDetailReferencePoint = "rev_ref_point_right";
    private const string RevSectionReferencePoint = "rev_ref_point_left";

    public override string Name => "M overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        var detailReferencePoint =
            context.IsReverse180
                ? RevDetailReferencePoint
                : StdDetailReferencePoint;

        var sectionReferencePoint =
            context.IsReverse180
                ? RevSectionReferencePoint
                : StdSectionReferencePoint;

        Logger.Info(
            "[Overlay][M] Reference-point selection -> " +
            $"Shank={(context.IsReverse180 ? "SW_180REV" : "SW_STD")}, " +
            $"Detail='{detailReferencePoint}', " +
            $"Section='{sectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            detailReferencePoint,
            sectionReferencePoint,
            primaryReferencePoint: detailReferencePoint);
    }
}
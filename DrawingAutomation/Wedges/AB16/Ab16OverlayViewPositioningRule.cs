using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges.AB16;

public sealed class Ab16OverlayViewPositioningRule : OverlayViewPositioningRuleBase
{
    private const string DetailReferencePoint = "ref_point_right";
    private const string SectionReferencePoint = "ref_point_left";

    public override string Name => "AB16 overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        Logger.Info(
            "[Overlay][AB16] Reference-point selection -> " +
            $"Detail='{DetailReferencePoint}', Section='{SectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            DetailReferencePoint,
            SectionReferencePoint,
            primaryReferencePoint: DetailReferencePoint);
    }
}
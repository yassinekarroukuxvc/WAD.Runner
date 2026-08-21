using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges._45CK;

public sealed class _45CKOverlayViewPositioningRule :
    OverlayViewPositioningRuleBase
{
    private const string DetailReferencePoint =
        "ref_point_right";

    private const string SectionReferencePoint =
        "ref_point_left";

    public override string Name =>
        "_45CK overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        Logger.Info(
            "[Overlay][_45CK] Reference-point selection -> " +
            $"Detail='{DetailReferencePoint}', " +
            $"Section='{SectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            DetailReferencePoint,
            SectionReferencePoint,
            primaryReferencePoint: DetailReferencePoint);
    }
}
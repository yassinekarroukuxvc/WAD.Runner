using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges._4516;

public sealed class _4516OverlayViewPositioningRule
    : OverlayViewPositioningRuleBase
{

    private const string DetailReferencePoint =
        "ref_point_right";

    private const string SectionReferencePoint =
        "ref_point_left";

    // ================================================================
    // NAME
    // ================================================================

    public override string Name =>
        "4516 overlay positioning";

    // ================================================================
    // PLACEMENTS
    // ================================================================

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {

        Logger.Info(
            "[Overlay][4516] Reference-point selection -> " +
            $"Detail='{DetailReferencePoint}', " +
            $"Section='{SectionReferencePoint}'.");

        return BuildStandardPlacements(
            context,
            DetailReferencePoint,
            SectionReferencePoint,
            primaryReferencePoint:
                DetailReferencePoint);
    }
}
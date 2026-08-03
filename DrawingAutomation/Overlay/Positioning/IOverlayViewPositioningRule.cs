using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Overlay.Positioning;

public sealed record OverlayViewPlacement(
    string LogicalViewName,
    string ReferencePointName,
    double XIn,
    double YIn);

public interface IOverlayViewPositioningRule
{
    string Name { get; }

    IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context);
}

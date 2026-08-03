using System.Collections.Generic;

namespace WAD.Runner.DrawingAutomation.Wedges;

public sealed record DrawingWedgeBehavior(
    string OverlayMagnificationSourceKey,
    IReadOnlyList<string> OverlayDimensionKeys,
    string OverlayReferencePointSketch,
    bool RepositionPrimaryOverlayViews,
    bool DeleteFrontOverlayViewWhenVrIsZero,
    bool HideVrExtremaWhenOverlayCompressed,
    decimal? BreaklineTlOverrideMm = null);

using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Views.Breaklines;
using SwView = global::SolidWorks.Interop.sldworks.View;

namespace WAD.Runner.DrawingAutomation.Wedges;

public sealed record DrawingWedgeBehavior(
    string OverlayMagnificationSourceKey,
    IReadOnlyList<string> OverlayDimensionKeys,
    string OverlayReferencePointSketch,
    bool RepositionPrimaryOverlayViews,
    bool DeleteFrontOverlayViewWhenVrIsZero,
    bool HideVrExtremaWhenOverlayCompressed,
    decimal? BreaklineTlOverrideMm = null,
    Func<global::SolidWorks.Interop.sldworks.DrawingDoc, SwView, string, WedgeData, DrawingData, BreaklineLayout, BreaklineLayout>?
        RefineBreakline = null,
    Func<WedgeData, DrawingData, double>? ResolveSecondaryRightMarginMm = null,
    Func<WedgeData, DrawingData, bool>? UseSplitDimensionTables = null,
    string? OverlayFormattedSketchSuffix = null,
    string? OverlayLineFormatProfilePath = null,
    // Display TL per article for breaklines (overrides BreaklineTlOverrideMm). With a fixed primary
    // scale the primary views are drawn at configured scale x BreaklineTlOverrideMm / this length,
    // so the drawn length stays the same.
    Func<WedgeData, double>? ResolveBreaklineTlMm = null,
    // Production/Customer: wedge-specific drawing marks added after annotations are positioned.
    Action<global::SolidWorks.Interop.sldworks.DrawingDoc, WedgeData, DrawingData>? DecorateProductionDrawing = null);

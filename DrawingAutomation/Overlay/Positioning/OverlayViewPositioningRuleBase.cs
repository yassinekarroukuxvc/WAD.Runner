using System;
using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.Profiles;

namespace WAD.Runner.DrawingAutomation.Overlay.Positioning;

public abstract class OverlayViewPositioningRuleBase : IOverlayViewPositioningRule
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context);

    protected static IReadOnlyList<OverlayViewPlacement> BuildStandardPlacements(
        OverlayViewPositioningContext context,
        string detailReferencePoint,
        string sectionReferencePoint,
        double detailXIn = OverlayPositioningDefaults.DetailXIn,
        double detailYIn = OverlayPositioningDefaults.DetailYIn,
        double sectionXIn = OverlayPositioningDefaults.SectionXIn,
        double sectionYIn = OverlayPositioningDefaults.SectionYIn,
        string? primaryReferencePoint = null,
        bool? includePrimaryViews = null)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var placements = new List<OverlayViewPlacement>
        {
            new(
                DrawingViewNames.Detail,
                detailReferencePoint,
                detailXIn,
                detailYIn),
            new(
                DrawingViewNames.Section,
                sectionReferencePoint,
                sectionXIn,
                sectionYIn)
        };

        if (includePrimaryViews ?? context.RepositionPrimaryViews)
        {
            var resolvedPrimaryReferencePoint =
                string.IsNullOrWhiteSpace(primaryReferencePoint)
                    ? context.BaseReferencePointName
                    : primaryReferencePoint;

            placements.Add(new OverlayViewPlacement(
                DrawingViewNames.Front,
                resolvedPrimaryReferencePoint,
                OverlayPositioningDefaults.FrontXIn,
                OverlayPositioningDefaults.FrontYIn));

            placements.Add(new OverlayViewPlacement(
                DrawingViewNames.Side,
                resolvedPrimaryReferencePoint,
                OverlayPositioningDefaults.SideXIn,
                OverlayPositioningDefaults.SideYIn));
        }

        return placements;
    }

    protected static bool IsPositiveFinite(double value)
        => double.IsFinite(value) && value > 0.0;

    protected static bool IsNonNegativeFinite(double value)
        => double.IsFinite(value) && value >= 0.0;

    protected static double MmToIn(double millimeters)
        => millimeters / OverlayPositioningConstants.MillimetersPerInch;
}

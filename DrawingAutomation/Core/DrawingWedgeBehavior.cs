using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Core;

/// <summary>
/// Broad drawing behavior shared by wedge types that use the same templates and rules.
/// Add a new wedge type here once, then register its concrete drawing profiles separately.
/// </summary>
public enum DrawingWedgeFamily
{
    Ckvd,
    CobLike,
    Osg7
}

public sealed record DrawingWedgeBehavior(
    DrawingWedgeFamily Family,
    string OverlayMagnificationSourceKey,
    string OverlayReferencePointSketch,
    bool RepositionPrimaryOverlayViews,
    bool DeleteFrontOverlayViewWhenVrIsZero);

public static class DrawingWedgeBehaviorCatalog
{
    private static readonly IReadOnlyDictionary<WedgeType, DrawingWedgeBehavior> Behaviors =
        new Dictionary<WedgeType, DrawingWedgeBehavior>
        {
            [WedgeType.CKVD] = new(
                DrawingWedgeFamily.Ckvd,
                OverlayMagnificationSourceKey: "FL",
                OverlayReferencePointSketch: "ref_point_2",
                RepositionPrimaryOverlayViews: true,
                DeleteFrontOverlayViewWhenVrIsZero: true),

            [WedgeType.COB] = CobLike(),
            [WedgeType.UTUS] = CobLike(),
            [WedgeType.FP] = CobLike(),

            [WedgeType.OSG7] = new(
                DrawingWedgeFamily.Osg7,
                OverlayMagnificationSourceKey: "FL",
                OverlayReferencePointSketch: "ref_point",
                RepositionPrimaryOverlayViews: false,
                DeleteFrontOverlayViewWhenVrIsZero: false)
        };

    private static readonly IReadOnlyList<WedgeType> SupportedTypes =
        new List<WedgeType>(Behaviors.Keys).AsReadOnly();

    public static IReadOnlyList<WedgeType> SupportedWedgeTypes => SupportedTypes;

    public static DrawingWedgeBehavior Get(WedgeType wedgeType)
    {
        if (Behaviors.TryGetValue(wedgeType, out var behavior))
            return behavior;

        throw new NotSupportedException(
            $"No drawing behavior is registered for wedge type '{wedgeType}'. " +
            "Register the new wedge type in DrawingWedgeBehaviorCatalog.");
    }

    private static DrawingWedgeBehavior CobLike()
        => new(
            DrawingWedgeFamily.CobLike,
            OverlayMagnificationSourceKey: "T",
            OverlayReferencePointSketch: "ref_point_sketch",
            RepositionPrimaryOverlayViews: false,
            DeleteFrontOverlayViewWhenVrIsZero: false);
}

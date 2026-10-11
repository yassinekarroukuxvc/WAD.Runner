using System;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Views;
using WAD.Runner.DrawingAutomation.Views.Breaklines;

namespace WAD.Runner.DrawingAutomation.Wedges._4516;

internal static class _4516SectionBreaklineRule
{
    // Leave room for the whole funnel/hole and its leaders above the sketch reference.
    private const double ClearanceMeters = 0.012;

    public static BreaklineLayout Refine(DrawingDoc document, View view, string logicalView,
        WedgeData wedge, DrawingData drawing, BreaklineLayout layout)
    {
        if ((logicalView != DrawingViewNames.Section && logicalView != DrawingViewNames.Detail) || wedge.Subclass != WedgeSubclass.FG ||
            drawing.DrawingType is not (DrawingType.Production or DrawingType.Customer))
            return layout;

        var section = ViewFinder.FindByName(document, "Section View AB-AB")
            ?? throw new InvalidOperationException("4516 section view is missing.");
        var model = section.ReferencedDocument as ModelDoc2
            ?? throw new InvalidOperationException("4516 section has no referenced model.");
        var feature = model.FirstFeature() as Feature;
        while (feature is not null && feature.Name != "funnel_cut_point")
            feature = feature.GetNextFeature() as Feature;
        var sketch = feature?.GetSpecificFeature2() as Sketch
            ?? throw new InvalidOperationException("4516 requires sketch 'funnel_cut_point'.");
        if (sketch.GetSketchPoints2() is not object[] points || points.Length != 1 ||
            points[0] is not SketchPoint point)
            throw new InvalidOperationException("4516 'funnel_cut_point' must contain exactly one point.");

        // Sketch coordinates -> model coordinates -> scaled view coordinates.
        // Remove sheet translation. The 4516 model origin is at the foot, whereas
        // break positions are relative to the midpoint of the fixed 18 mm model span.
        var modelPoint = Transform(new[] { point.X, point.Y, point.Z },
            (MathTransform)sketch.ModelToSketchTransform.Inverse());
        var viewTransform = section.ModelToViewTransform;
        var projected = Transform(modelPoint, viewTransform);
        var origin = Transform(new double[3], viewTransform);
        var pointHeight = projected[1] - origin[1];
        // The funnel is in the retained lower section; +Y runs towards the shank.
        // Equal paper-space retained heights align both the feet and the break edges,
        // even when Detail and Section use different scales. Keep at least the original
        // 60 mm foot view height; enlarge it when the funnel reference needs more room.
        var retainedHeight = Math.Max(0.060, pointHeight + ClearanceMeters);
        var lower = -layout.UpperPosition + retainedHeight;
        if (!double.IsFinite(lower) || lower >= layout.UpperPosition)
            throw new InvalidOperationException("4516 funnel point leaves no valid section break interval.");
        Logger.Info($"[4516 FunnelBreak] sketch=({point.X:F6},{point.Y:F6},{point.Z:F6}), " +
            $"model=({modelPoint[0]:F6},{modelPoint[1]:F6},{modelPoint[2]:F6}), " +
            $"pointHeight={pointHeight:F6}, retainedHeight={retainedHeight:F6}, lower={lower:F6}, " +
            $"clearance>=12.000 mm sheet, view={logicalView}, scale={layout.ViewScale:F3}.");
        return layout with { LowerPosition = lower };
    }

    private static double[] Transform(double[] p, MathTransform transform)
    {
        var a = (double[])transform.ArrayData;
        return new[]
        {
            (p[0] * a[0] + p[1] * a[3] + p[2] * a[6]) * a[12] + a[9],
            (p[0] * a[1] + p[1] * a[4] + p[2] * a[7]) * a[12] + a[10],
            (p[0] * a[2] + p[1] * a[5] + p[2] * a[8]) * a[12] + a[11]
        };
    }
}

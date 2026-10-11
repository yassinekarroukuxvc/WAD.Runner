using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Wedges._4516;

/// <summary>
/// FG Production/Customer: small center marks on the FR and BR fillets in the section view.
/// The arc center comes from the sketch arc the radius dimension is attached to; the matching
/// section edge is then picked on the sheet and gets a SolidWorks center mark.
/// </summary>
internal static class _4516FilletCenterMarkRule
{
    private const string SectionViewName = "Section View AB-AB";
    private const int CenterMarkSingle = 2;           // swCenterMark_Single
    // Paper size of the cross. SolidWorks scales center marks by the view scale, so the
    // value set on the mark is divided by it.
    private const double MarkSizeMeters = 0.00045;
    private const double RadiusToleranceMm = 0.001;

    private static readonly string[] Keys = { "FR", "BR" };

    public static void Apply(DrawingDoc drawing, WedgeData wedge, DrawingData drawingData)
    {
        if (wedge.Subclass != WedgeSubclass.FG ||
            drawingData.DrawingType is not (DrawingType.Production or DrawingType.Customer))
            return;

        var section = ViewFinder.FindByName(drawing, SectionViewName);
        if (section is null)
        {
            Logger.Warn("[4516 CenterMarks] Section view not found.");
            return;
        }

        var model = (ModelDoc2)drawing;
        var marked = new List<double[]>();

        foreach (var key in Keys)
        {
            var radiusMm = RadiusMm(wedge, key);
            if (radiusMm <= 0)
                continue;

            var arc = FindDimensionArc(section, key);
            if (arc is null || !TryGetSheetCenter(section, arc, out var center))
            {
                Logger.Warn($"[4516 CenterMarks] No arc found for {key} (R={radiusMm:F4} mm).");
                continue;
            }

            if (marked.Exists(c => Math.Abs(c[0] - center[0]) < 2e-4 && Math.Abs(c[1] - center[1]) < 2e-4))
                continue;
            marked.Add(center);

            if (TryInsertCenterMarkOnEdgeAt(drawing, model, center, radiusMm / 1000.0, section.ScaleDecimal))
                Logger.Info($"[4516 CenterMarks] {key} center mark at ({center[0] * 1000:F2},{center[1] * 1000:F2}) mm.");
            else
                Logger.Warn($"[4516 CenterMarks] Could not mark {key} at ({center[0] * 1000:F2},{center[1] * 1000:F2}) mm.");
        }

        model.ClearSelection2(true);
    }

    private static double RadiusMm(WedgeData wedge, string key)
    {
        var dim = wedge.TryGet(DimensionKey.From(key));
        return dim is not null && dim.Nominal.Unit == UnitKind.Millimeter ? (double)dim.Nominal.Value : 0.0;
    }

    // The FR/BR radius dimension is attached to the arc it measures.
    private static object? FindDimensionArc(View section, string key)
    {
        if (section.GetAnnotations() is not object[] annotations)
            return null;

        foreach (var o in annotations)
        {
            if (o is not Annotation annotation ||
                annotation.GetSpecificAnnotation() is not DisplayDimension display ||
                display.GetDimension2(0) is not global::SolidWorks.Interop.sldworks.Dimension dimension)
                continue;

            var name = dimension.FullName ?? dimension.Name ?? string.Empty;
            if (!name.StartsWith(key + "@", StringComparison.OrdinalIgnoreCase))
                continue;

            if (annotation.GetAttachedEntities3() is not object[] entities)
                continue;

            foreach (var entity in entities)
            {
                if (entity is SketchArc || (entity is Edge edge && edge.GetCurve() is Curve c && c.IsCircle()))
                    return entity;
            }
        }

        return null;
    }

    private static bool TryGetSheetCenter(View section, object arc, out double[] center)
    {
        center = Array.Empty<double>();
        double[] modelCenter;

        if (arc is SketchArc sketchArc)
        {
            if (sketchArc.GetCenterPoint2() is not SketchPoint point ||
                ((SketchSegment)sketchArc).GetSketch() is not Sketch sketch)
                return false;

            modelCenter = Transform(new[] { point.X, point.Y, point.Z },
                (MathTransform)sketch.ModelToSketchTransform.Inverse());
        }
        else if (arc is Edge edge && edge.GetCurve() is Curve curve && curve.IsCircle())
        {
            var p = (double[])curve.CircleParams;
            modelCenter = new[] { p[0], p[1], p[2] };
        }
        else
        {
            return false;
        }

        center = Transform(modelCenter, section.ModelToViewTransform);
        center[1] += FootBreakShift(section);
        return double.IsFinite(center[0]) && double.IsFinite(center[1]);
    }

    // ModelToViewTransform ignores the break. SolidWorks closes a break symmetrically, so the
    // retained lower part (the foot, where FR and BR are) moves up by half of the removed span
    // minus the gap.
    private static double FootBreakShift(View section)
    {
        if (section.GetBreakLines() is not object[] breaks || breaks.Length != 1 ||
            breaks[0] is not BreakLine breakLine)
            return 0.0;

        var removed = Math.Abs(breakLine.GetPosition(1) - breakLine.GetPosition(0));
        return Math.Max(0.0, removed - section.BreakLineGap) / 2.0;
    }

    // The dimension's sketch arc cannot be selected in the view, so pick the section edge
    // on the sheet: probe points on the arc around its center and keep the first pick that is a
    // circular edge of the expected radius.
    private static bool TryInsertCenterMarkOnEdgeAt(
        DrawingDoc drawing, ModelDoc2 model, double[] center, double radiusModel, double viewScale)
    {
        var radiusPaper = radiusModel * viewScale;
        if (!(radiusPaper > 0))
            return false;

        try
        {
            drawing.EditSheet();
            for (var i = 0; i < 16; i++)
            {
                var angle = i * Math.PI / 8.0;
                var x = center[0] + radiusPaper * Math.Cos(angle);
                var y = center[1] + radiusPaper * Math.Sin(angle);

                model.ClearSelection2(true);
                if (!model.Extension.SelectByID2("", "EDGE", x, y, 0, false, 0, null, 0))
                    continue;

                if (((SelectionMgr)model.SelectionManager).GetSelectedObject6(1, -1) is not Edge edge ||
                    edge.GetCurve() is not Curve curve || !curve.IsCircle() ||
                    Math.Abs(((double[])curve.CircleParams)[6] - radiusModel) > RadiusToleranceMm / 1000.0)
                    continue;

                if (drawing.InsertCenterMark3(CenterMarkSingle, false, false) is not CenterMark mark)
                    return false;

                mark.UseDocDisplaySettings = false;
                mark.ShowLines = false;
                mark.Size = MarkSizeMeters / viewScale;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[4516 CenterMarks] Edge pick failed: {ex.Message}");
            return false;
        }
        finally
        {
            model.ClearSelection2(true);
        }
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

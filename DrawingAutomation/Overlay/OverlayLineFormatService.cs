using System;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Overlay;

/// <summary>Copies explicit part-sketch formats into each drawing view without editing the part.</summary>
public static class OverlayLineFormatService
{
    private const int Pink = 0xFF00FF; // SolidWorks COLORREF: RGB(255, 0, 255).
    // SetLineStyle uses internal names from GetLineStyles, rather than GetLineFontName2's UI labels.
    private static readonly string[] BuiltInStyles =
        { "CONTINUOUS", "HIDDEN", "PHANTOM", "CHAIN", "CENTER", "STITCH", "CHAIN_THICK" };

    public static void Apply(DrawingService service, string? sketchSuffix, string? profilePath = null)
    {
        var model = service.Model ?? throw new InvalidOperationException("Overlay drawing is not open.");
        var drawing = service.Drawing ?? throw new InvalidOperationException("Overlay drawing is not open.");
        var profile = string.IsNullOrEmpty(profilePath) ? null : OverlayLineFormatProfile.Load(profilePath);
        var styles = profile?.RegisterStyles(model, drawing);
        if (profile != null) ApplySheetFormats(model, drawing, profile, styles!);
        int sketches = 0, hidden = 0;
        try
        {
            for (var view = drawing.GetFirstView() as View; view != null; view = view.GetNextView() as View)
            {
                var root = view.RootDrawingComponent;
                if (root == null) continue; // Sheet-format view.
                hidden += ApplyHiddenEdges(model, drawing, view, profile, styles);
                if (string.IsNullOrEmpty(sketchSuffix) || view.ReferencedDocument is not ModelDoc2 part)
                    continue;

                for (var feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                {
                    if (!feature.Name.EndsWith(sketchSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                    // Evaluate the view's configuration, not whichever configuration is active in the part.
                    var suppression = feature.IsSuppressed2((int)swInConfigurationOpts_e.swSpecifyConfiguration,
                        new[] { view.ReferencedConfiguration }) as bool[];
                    if (suppression is { Length: > 0 } && suppression[0]) continue;
                    if (feature.GetSpecificFeature2() is not Sketch sketch ||
                        sketch.GetSketchSegments() is not object[] segments) continue;
                    foreach (var item in segments)
                    {
                        if (item is not SketchSegment segment || segment.ConstructionGeometry) continue;
                        int color = segment.Color, style = segment.Style, width = segment.Width;
                        if (profile != null) color = OverlayLineFormatProfile.Color(profile.OutlineRgb);
                        model.ClearSelection2(true);
                        string id = $"{segment.GetName()}@{feature.Name}@{root.Name}@{view.Name}";
                        if (!model.Extension.SelectByID2(id, "EXTSKETCHSEGMENT", 0, 0, 0, false, 0, null, 0))
                            throw new InvalidOperationException($"Cannot select overlay sketch segment '{id}'.");
                        // -1 means inherited: leave that property at the drawing's existing default.
                        if (profile != null && style == (int)swLineStyles_e.swLineHIDDEN)
                            drawing.SetLineStyle(styles![feature.Name.StartsWith("fl_", StringComparison.OrdinalIgnoreCase) ? "FL" : "Tolerance"]);
                        else if (style >= 0 && style < BuiltInStyles.Length)
                            drawing.SetLineStyle(BuiltInStyles[style]);
                        if (width >= 0) drawing.SetLineWidth(width);
                        // Setting style can reset the color; apply explicit color last.
                        if (color >= 0) drawing.SetLineColor(color);
                        sketches++;
                    }
                }
            }
        }
        finally { model.ClearSelection2(true); }
        service.Rebuild(redraw: true);
        Logger.Info($"[OverlayFormat] Formatted {sketches} sketch segments and {hidden} hidden model edges; reference={profile?.SourceDrawing ?? "part template"}.");
    }

    private static int ApplyHiddenEdges(ModelDoc2 model, DrawingDoc drawing, View view,
        OverlayLineFormatProfile? profile, System.Collections.Generic.Dictionary<string, string>? styles)
    {
        var edges = view.GetPolylines7(1, out var raw) as object[];
        if (edges == null || raw is not double[] data) return 0;
        int offset = 0, changed = 0;
        model.ClearSelection2(true);
        for (int i = 0; i < edges.Length; i++)
        {
            if (offset + 2 > data.Length) throw new InvalidOperationException("Incomplete overlay polyline geometry.");
            int type = (int)data[offset++], geometrySize = (int)data[offset++];
            // Some SW versions return a nonzero dummy size for straight polylines; they still have no geometry block.
            if (type != 0) offset += geometrySize;
            if (offset + 7 > data.Length) throw new InvalidOperationException("Incomplete overlay polyline attributes.");
            int lineCategory = (int)data[offset + 1];
            int points = (int)data[offset + 6];
            offset += 7 + checked(points * 3);
            if (points < 0 || offset > data.Length) throw new InvalidOperationException("Invalid overlay polyline points.");
            // The legacy font table identifies edge categories (Hidden / TanHidden), not dash patterns.
            // In particular, a user-dashed visible edge or a dashed sketch must not be recolored.
            if (lineCategory < 0) continue;
            string category = drawing.GetLineFontName(lineCategory);
            if (category != "Hidden" && category != "TanHidden") continue;
            bool selected = edges[i] != null && view.SelectEntity(edges[i], changed > 0);
            if (!selected && edges[i] is Entity entity)
            {
                var selection = ((SelectionMgr)model.SelectionManager).CreateSelectData();
                selection.View = view;
                selected = entity.Select4(changed > 0, selection);
            }
            if (!selected && edges[i] is SilhouetteEdge silhouette)
            {
                var selection = ((SelectionMgr)model.SelectionManager).CreateSelectData();
                selection.View = view;
                selected = silhouette.Select2(changed > 0, selection);
            }
            if (!selected && edges[i] != null)
            {
                var manager = (SelectionMgr)model.SelectionManager;
                var selection = manager.CreateSelectData();
                selection.View = view;
                selected = manager.AddSelectionListObject(edges[i], selection);
            }
            if (!selected) throw new InvalidOperationException($"Cannot select hidden edge {i} in '{view.Name}'.");
            changed++;
        }
        // Apply once after collecting all edges: changing display data can invalidate the remaining COM edges.
        if (changed > 0)
        {
            drawing.SetLineStyle(styles?["Hidden"] ?? BuiltInStyles[(int)swLineStyles_e.swLineHIDDEN]);
            drawing.SetLineColor(profile == null ? Pink : OverlayLineFormatProfile.Color(profile.HiddenRgb));
        }
        return changed;
    }

    private static void ApplySheetFormats(ModelDoc2 model, DrawingDoc drawing,
        OverlayLineFormatProfile profile, System.Collections.Generic.Dictionary<string, string> styles)
    {
        var manager = (LayerMgr)model.GetLayerManager();
        var layerNames = manager.GetLayerList() as string[] ?? Array.Empty<string>();
        foreach (var (name, role) in profile.LayerRoles)
        {
            string? actualName = Array.Find(layerNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (actualName == null || manager.GetLayer(actualName) is not Layer layer)
                throw new InvalidOperationException($"Missing reference overlay layer '{name}'.");
            int color = profile.RoleColor(role);
            layer.Color = color;
            if (layer.GetItems((int)swLayerItemsOption_e.swLayerItemsOption_Annotations) is object[] layerAnnotations)
                foreach (var item in layerAnnotations)
                    if (item is Annotation annotation) annotation.Color = color;
        }
        // Layer.GetItems omits sheet-format sketch entities; access their owning sketch directly.
        drawing.EditTemplate();
        try
        {
            var sketch = ((Sheet)drawing.GetCurrentSheet()).GetTemplateSketch();
            if (sketch.GetSketchSegments() is object[] items)
                foreach (var item in items)
                {
                    if (item is not SketchSegment segment) continue;
                    string? role = ResolveLayerRole(profile, segment.Layer);
                    if (role == null) continue;
                    model.ClearSelection2(true);
                    if (!segment.Select4(false, null)) throw new InvalidOperationException("Cannot select sheet-format segment.");
                    if (role == "Calibration") drawing.SetLineStyle(styles["Calibration"]);
                    drawing.SetLineColor(profile.RoleColor(role));
                }
            ApplyAnnotations(model, drawing, profile);
        }
        finally { model.ClearSelection2(true); drawing.EditSheet(); }
        ApplyAnnotations(model, drawing, profile);
    }

    private static string? ResolveLayerRole(OverlayLineFormatProfile profile, string layer)
    {
        foreach (var mapping in profile.LayerRoles)
            if (string.Equals(layer, mapping.Key, StringComparison.OrdinalIgnoreCase)) return mapping.Value;
        return null;
    }

    private static void ApplyAnnotations(ModelDoc2 model, DrawingDoc drawing, OverlayLineFormatProfile profile)
    {
        for (var view = drawing.GetFirstView() as View; view != null; view = view.GetNextView() as View)
        {
            if (view.GetAnnotations() is not object[] annotations) continue;
            foreach (var item in annotations)
            {
                if (item is not Annotation annotation) continue;
                string role = ResolveLayerRole(profile, annotation.Layer) ?? "Annotation";
                if (annotation.GetSpecificAnnotation() is Note note &&
                    (note.GetText() ?? "").Contains("ACAD FILE", StringComparison.OrdinalIgnoreCase)) role = "Outline";
                int color = profile.RoleColor(role);
                if (annotation.GetSpecificAnnotation() is Note formattedNote)
                {
                    string linkedText = formattedNote.PropertyLinkedText ?? "";
                    string recolored = Regex.Replace(linkedText, @"(\bcolor\s*=\s*)0x[0-9a-f]+",
                        match => match.Groups[1].Value + $"0x{color:X8}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (recolored != linkedText) formattedNote.PropertyLinkedText = recolored;
                }
                annotation.Color = color;
                annotation.LayerOverride |= 1;
                model.ClearSelection2(true);
                if (annotation.Select3(false, null)) drawing.SetLineColor(color);
            }
        }
        model.ClearSelection2(true);
    }
}

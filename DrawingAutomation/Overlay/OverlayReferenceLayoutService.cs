using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Overlay;

/// <summary>
/// Places the overlay's description and coining blocks and the calibration label where the reference DWG has them.
/// Runs after the shared annotation text style, which would otherwise restyle the calibration label.
/// </summary>
public static class OverlayReferenceLayoutService
{
    public static void Apply(DrawingService service, string profilePath)
    {
        var model = service.Model ?? throw new InvalidOperationException("Overlay drawing is not open.");
        var drawing = service.Drawing ?? throw new InvalidOperationException("Overlay drawing is not open.");
        var profile = OverlayLineFormatProfile.Load(profilePath);
        var layout = profile.ReferenceLayout;
        if (layout == null) return;

        string calibrationLayer = profile.LayerRoles.FirstOrDefault(r => r.Value == "Calibration").Key
            ?? throw new InvalidOperationException("Overlay profile has no Calibration layer.");
        var frame = TemplateLayerBounds(drawing, layout.FrameLayer);
        var sheet = (Sheet)drawing.GetCurrentSheet();
        var sheetProps = (double[])sheet.GetProperties2();
        // Block instance positions are in sheet-scaled space; block geometry and Scale2 are on paper.
        double sheetScale = sheetProps[2] / sheetProps[3];

        try
        {
            // The blocks keep the reference arrangement, shrunk toward the frame's lower-left corner.
            double s = layout.BlockScale;
            if (!(s > 0 && s <= 1)) throw new InvalidOperationException($"Invalid overlay blockScale {s}.");
            var blockFrame = new[] { frame[0], frame[1], frame[0] + s * (frame[2] - frame[0]), frame[1] + s * (frame[3] - frame[1]) };
            if (layout.DescriptionBox != null) PlaceBlock(model, drawing, profile, blockFrame, sheetScale, layout.DescriptionBox);
            if (layout.CoiningBox != null) PlaceBlock(model, drawing, profile, blockFrame, sheetScale, layout.CoiningBox);
            if (layout.CalibrationNote != null)
                FormatCalibrationNote(drawing, TemplateLayerBounds(drawing, calibrationLayer), calibrationLayer, layout);
        }
        finally { model.ClearSelection2(true); }
        service.Rebuild(redraw: true);
    }

    private static void PlaceBlock(ModelDoc2 model, DrawingDoc drawing, OverlayLineFormatProfile profile,
        double[] frame, double sheetScale, OverlayReferenceBlock reference)
    {
        if (reference.Box.Length != 4) throw new InvalidOperationException($"Invalid reference box for '{reference.NoteText}'.");
        var matches = (model.SketchManager.GetSketchBlockDefinitions() as object[] ?? Array.Empty<object>())
            .OfType<SketchBlockDefinition>()
            .Where(d => d.GetInstanceCount() > 0 && (d.GetNotes() as object[] ?? Array.Empty<object>())
                .OfType<Note>().Any(n => string.Equals((n.GetText() ?? "").Trim(), reference.NoteText, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matches.Count != 1 || matches[0].GetInstances() is not object[] { Length: 1 } instances ||
            instances[0] is not SketchBlockInstance instance)
            throw new InvalidOperationException($"Expected one placed overlay block containing '{reference.NoteText}', found {matches.Count}.");
        var definition = matches[0];

        var points = (definition.GetSketch().GetSketchSegments() as object[] ?? Array.Empty<object>())
            .OfType<SketchLine>()
            .SelectMany(l => new[] { (SketchPoint)l.GetStartPoint2(), (SketchPoint)l.GetEndPoint2() })
            .ToList();
        if (points.Count == 0) throw new InvalidOperationException($"Overlay block '{reference.NoteText}' has no box lines.");
        double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
        double width = points.Max(p => p.X) - minX, height = points.Max(p => p.Y) - minY;
        var insertion = (double[])((MathPoint)definition.InsertionPoint).ArrayData;

        var (x0, y0, unitScale) = profile.ToSheet(frame, reference.Box[0], reference.Box[1]);
        var (x1, y1, _) = profile.ToSheet(frame, reference.Box[2], reference.Box[3]);
        // Average both axes: the template block's aspect is within 0.5% of the reference's.
        double scale = ((x1 - x0) / width + (y1 - y0) / height) / 2;
        var position = (MathPoint)instance.InstancePosition;
        position.ArrayData = new[]
        {
            (x0 - scale * (minX - insertion[0])) / sheetScale,
            (y0 - scale * (minY - insertion[1])) / sheetScale,
            0.0
        };
        instance.Scale2 = scale;
        instance.InstancePosition = position;

        // Block notes and line formats can only be changed while the definition is open for editing.
        model.ClearSelection2(true);
        if (!instance.Select(false, null))
            throw new InvalidOperationException($"Cannot select overlay block '{reference.NoteText}'.");
        model.SketchManager.EditSketchBlock();
        try
        {
            // Label notes only accept a new format through the sheet view while the block is open;
            // definition.GetNotes() returns copies that silently ignore it.
            var labels = (definition.GetNotes() as object[] ?? Array.Empty<object>()).OfType<Note>()
                .Where(n => !n.IsAttribute()).Select(n => n.GetText() ?? "").ToHashSet();
            int resized = 0;
            foreach (var item in ((View)drawing.GetFirstView()).GetAnnotations() as object[] ?? Array.Empty<object>())
                if (item is Annotation annotation && annotation.GetSpecificAnnotation() is Note note && labels.Contains(note.GetText() ?? ""))
                {
                    ResizeMonoText(annotation, profile.ReferenceLayout!, reference.NoteText);
                    resized++;
                }
            if (resized != labels.Count)
                throw new InvalidOperationException($"Resized {resized} of {labels.Count} labels in '{reference.NoteText}'.");
            if (reference.SecondLinePitchUnits > 0) PlaceSecondLine(drawing, definition, reference, unitScale, x0, x1);
            if (reference.ValueX.Count > 0) AlignValues(drawing, profile, frame, reference);
            if (reference.BorderWidthUnits > 0)
            {
                model.ClearSelection2(true);
                int selected = 0;
                foreach (var item in definition.GetSketch().GetSketchSegments() as object[] ?? Array.Empty<object>())
                    if (item is SketchLine && ((SketchSegment)item).Select4(selected > 0, null)) selected++;
                if (selected != points.Count / 2)
                    throw new InvalidOperationException($"Selected {selected} of {points.Count / 2} border lines in '{reference.NoteText}'.");
                drawing.SetLineWidthCustom(reference.BorderWidthUnits * unitScale);
            }
        }
        finally
        {
            model.ClearSelection2(true);
            model.SketchManager.EndEditSketchBlock(true);
        }
        // Attribute values (DATE, INT, ...) are formatted per instance.
        foreach (var note in (instance.GetAttributes() as object[] ?? Array.Empty<object>()).OfType<Note>())
            ResizeMonoText((Annotation)note.GetAnnotation(), profile.ReferenceLayout!, reference.NoteText);
        Logger.Info(FormattableString.Invariant(
            $"[OverlayLayout] '{reference.NoteText}' block at ({x0 * 1000:0.###}, {y0 * 1000:0.###}) mm, {(x1 - x0) * 1000:0.###} x {(y1 - y0) * 1000:0.###} mm, scale {scale:0.###}, border {reference.BorderWidthUnits * unitScale * 1000:0.###} mm."));
    }

    /// <summary>Moves the block's one value note under its label, as the reference's two-line MTEXT.</summary>
    private static void PlaceSecondLine(DrawingDoc drawing, SketchBlockDefinition definition, OverlayReferenceBlock reference,
        double unitScale, double boxLeft, double boxRight)
    {
        var values = (definition.GetNotes() as object[] ?? Array.Empty<object>()).OfType<Note>()
            .Where(n => n.IsAttribute()).Select(n => n.GetText() ?? "").ToHashSet();
        Annotation? label = null, value = null;
        // While the block is open, the sheet view reports its notes in paper coordinates.
        foreach (var item in ((View)drawing.GetFirstView()).GetAnnotations() as object[] ?? Array.Empty<object>())
            if (item is Annotation annotation && annotation.GetSpecificAnnotation() is Note note)
            {
                if ((note.GetText() ?? "").Trim() == reference.NoteText) label = annotation;
                else if (values.Contains(note.GetText() ?? ""))
                    value = value == null ? annotation : throw new InvalidOperationException($"Overlay block '{reference.NoteText}' has more than one value note.");
            }
        if (label == null || value == null)
            throw new InvalidOperationException($"Cannot find the label and value notes of overlay block '{reference.NoteText}'.");
        var anchor = (double[])label.GetPosition();
        // Monotxt is monospaced with a one-height cell. Indent like the reference, but only as far as the value
        // still fits inside the box with the label's margin on both sides.
        double margin = anchor[0] - boxLeft, cell = reference.TextHeightUnits * unitScale;
        int cells = (int)Math.Floor((boxRight - boxLeft - 2 * margin) / cell);
        int indent = Math.Clamp(cells - (((Note)value.GetSpecificAnnotation()).GetText() ?? "").Trim().Length, 0, reference.SecondLineIndentCells);
        if (!value.SetPosition2(anchor[0] + indent * cell,
                anchor[1] - reference.SecondLinePitchUnits * unitScale, 0))
            throw new InvalidOperationException($"Cannot move the second line of overlay block '{reference.NoteText}'.");
    }

    /// <summary>Moves each value note to the reference ATTRIB's x, keeping its line.</summary>
    private static void AlignValues(DrawingDoc drawing, OverlayLineFormatProfile profile, double[] frame, OverlayReferenceBlock reference)
    {
        var pending = new HashSet<string>(reference.ValueX.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var item in ((View)drawing.GetFirstView()).GetAnnotations() as object[] ?? Array.Empty<object>())
        {
            if (item is not Annotation annotation || annotation.GetSpecificAnnotation() is not Note note) continue;
            var property = Regex.Match(note.PropertyLinkedText ?? "", @"\$PRP:""([^""]+)""");
            if (!property.Success || !reference.ValueX.TryGetValue(property.Groups[1].Value, out double x)) continue;
            var position = (double[])annotation.GetPosition();
            if (!annotation.SetPosition2(profile.ToSheet(frame, x, 0).X, position[1], 0))
                throw new InvalidOperationException($"Cannot move value '{property.Groups[1].Value}' in '{reference.NoteText}'.");
            pending.Remove(property.Groups[1].Value);
        }
        if (pending.Count > 0)
            throw new InvalidOperationException($"Overlay block '{reference.NoteText}' has no value for {string.Join(", ", pending)}.");
    }

    private static void ResizeMonoText(Annotation annotation, OverlayReferenceLayout layout, string block)
    {
        var format = (TextFormat)annotation.GetTextFormat(0);
        format.CharHeight *= layout.MonoTextHeightScale;
        format.WidthFactor *= layout.MonoTextWidthScale;
        if (!annotation.SetTextFormat(0, false, format))
            throw new InvalidOperationException($"Cannot resize a text in overlay block '{block}'.");
    }

    private static void FormatCalibrationNote(DrawingDoc drawing, double[] box, string layer, OverlayReferenceLayout layout)
    {
        var reference = layout.CalibrationNote!;
        double side = ((box[2] - box[0]) + (box[3] - box[1])) / 2;
        drawing.EditTemplate();
        try
        {
            var notes = new List<(Annotation Annotation, Note Note)>();
            for (var view = drawing.GetFirstView() as View; view != null; view = view.GetNextView() as View)
                foreach (var item in view.GetAnnotations() as object[] ?? Array.Empty<object>())
                    if (item is Annotation a && string.Equals(a.Layer, layer, StringComparison.OrdinalIgnoreCase) &&
                        a.GetSpecificAnnotation() is Note n && !notes.Any(x => x.Note.GetText() == n.GetText()))
                        notes.Add((a, n));
            if (notes.Count != 1)
                throw new InvalidOperationException($"Expected one calibration label on layer '{layer}', found {notes.Count}.");
            var (annotation, note) = notes[0];

            var value = Regex.Match(note.GetText() ?? "", @"\d+(?:[.,]\d+)?");
            if (!value.Success) throw new InvalidOperationException($"Calibration label '{note.GetText()}' has no value.");
            if (!note.SetText(value.Value + reference.UnitSuffix))
                throw new InvalidOperationException("Cannot set the calibration label text.");

            var format = (TextFormat)annotation.GetTextFormat(0);
            format.TypeFaceName = reference.Font;
            format.Bold = false;
            format.Italic = false;
            format.CharHeight = reference.HeightFraction * side * layout.MonoTextHeightScale;
            format.WidthFactor = reference.WidthFactor * layout.MonoTextWidthScale;
            if (!annotation.SetTextFormat(0, false, format))
                throw new InvalidOperationException("Cannot set the calibration label format.");
            note.SetTextJustification((int)swTextJustification_e.swTextJustificationRight);
            // Anchored at its top-right: the reference MTEXT hangs from its cap top, and a SW note's anchor is above that.
            double x = box[0] + reference.RightFraction * (box[2] - box[0]);
            double y = box[1] + reference.YFraction * (box[3] - box[1]) + layout.MonoTextAnchorAboveCap * format.CharHeight;
            if (!annotation.SetPosition2(x, y, 0))
                throw new InvalidOperationException("Cannot position the calibration label.");
            Logger.Info(FormattableString.Invariant(
                $"[OverlayLayout] Calibration label '{note.GetText()}' at ({x * 1000:0.###}, {y * 1000:0.###}) mm, height {format.CharHeight * 1000:0.###} mm."));
        }
        finally { drawing.EditSheet(); }
    }

    /// <summary>Bounds (x0, y0, x1, y1, meters) of the sheet-format lines on one layer.</summary>
    private static double[] TemplateLayerBounds(DrawingDoc drawing, string layer)
    {
        drawing.EditTemplate();
        try
        {
            var points = (((Sheet)drawing.GetCurrentSheet()).GetTemplateSketch().GetSketchSegments() as object[] ?? Array.Empty<object>())
                .OfType<SketchLine>()
                .Where(l => string.Equals(((SketchSegment)l).Layer, layer, StringComparison.OrdinalIgnoreCase))
                .SelectMany(l => new[] { (SketchPoint)l.GetStartPoint2(), (SketchPoint)l.GetEndPoint2() })
                .ToList();
            if (points.Count == 0) throw new InvalidOperationException($"No sheet-format lines on layer '{layer}'.");
            return new[] { points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y) };
        }
        finally { drawing.EditSheet(); }
    }
}

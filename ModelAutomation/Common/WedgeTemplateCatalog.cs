using System;
using System.Collections.Generic;
using System.IO;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Common;

/// <summary>
/// Single source of truth for the SolidWorks template files used per wedge type.
/// Used by the CLI (run-model, run-drawing) and the API executor.
/// </summary>
public static class WedgeTemplateCatalog
{
    public sealed record TemplateSet(
        string PartPath,
        string EquationsPath,
        string? DrawingPath,
        string? OverlayDrawingPath)
    {
        /// <summary>Production and Customer use the same drawing template; Overlay has its own.</summary>
        public string? DrawingPathFor(DrawingType drawingType) =>
            drawingType == DrawingType.Overlay ? OverlayDrawingPath : DrawingPath;
    }

    private static readonly string Root = Path.Combine("Resources", "Templates");

    private static readonly IReadOnlyDictionary<WedgeType, TemplateSet> Templates =
        new Dictionary<WedgeType, TemplateSet>
        {
            [WedgeType.COB] = Set(
                Path.Combine("COB", "COB_rev2"),
                "COB_part_rev1.SLDPRT", "equations.txt",
                "COB_drawing_rev1.SLDPRT.SLDDRW", "COB_overlay_rev1.SLDDRW"),

            [WedgeType.UTUS] = Set(
                Path.Combine("UT-US", "UTUS_rev2"),
                "UTUS_part_rev1.SLDPRT", "equations.txt",
                "UTUS_drawing_rev1.SLDPRT.SLDDRW", "UTUS_overlay_rev1.SLDDRW"),

            [WedgeType.FP] = Set(
                Path.Combine("FP", "FP_rev2"),
                "FP_part_rev1.SLDPRT", "equations.txt",
                "FP_drawing_rev1.SLDPRT.SLDDRW", "FP_overlay_rev1.SLDDRW"),

            [WedgeType.OSG7] = Set(
                Path.Combine("OSG7", "Working Version"),
                "OSG7.SLDPRT", "equations.txt",
                "OSG7.SLDDRW", "OSG7_overlay.SLDDRW"),

            [WedgeType._4516] = Set(
                "4516",
                "4516_part_rev2.SLDPRT", "equations2.txt",
                "4516_drawing_rev2.SLDPRT.SLDDRW", "4516_overlay_rev2.SLDDRW"),

            [WedgeType.ABT] = Set(
                Path.Combine("ABT", "ABT_rev1"),
                "ABT_part_rev1.SLDPRT", "equations.txt",
                "ABT_drawing_rev1.SLDPRT.SLDDRW", "ABT_overlay_rev1.SLDDRW"),

            [WedgeType.AB16] = Set(
                Path.Combine("AB16", "AB16_rev1"),
                "AB16_part_rev1.SLDPRT", "equations.txt",
                "AB16_drawing_rev2.SLDPRT.SLDDRW", "AB16_overlay_rev2.SLDDRW"),

            [WedgeType._45CK] = Set(
                Path.Combine("45CK", "45CK_rev1"),
                "CK45_part_rev1.SLDPRT", "equations.txt",
                "CK45_drawing_rev2.SLDPRT.SLDDRW", "CK45_overlay_rev2.SLDDRW"),

            [WedgeType.M] = Set(
                Path.Combine("M", "M_rev1"),
                "M_part_rev1.SLDPRT", "equations.txt",
                "M_drawing_rev2.SLDPRT.SLDDRW", "M_overlay_rev2.SLDDRW"),

            [WedgeType._1001] = Set(
                "1001",
                "1001A_part_rev1.SLDPRT", "equations.txt",
                "1001A_drawing_rev2.SLDPRT.SLDDRW", "1001A_overlay_rev2.SLDDRW"),

            [WedgeType.CKVD] = Set(
                Path.Combine("CKVD", "CKVD_rev2"),
                "ckvd_part_rev2.SLDPRT", "equations.txt",
                "ckvd_drawing_rev2.SLDPRT.SLDDRW", "ckvd_overlay_rev2.SLDDRW"),

            // VM: model phase only — no drawing module is registered yet.
            [WedgeType.VM] = Set(
                "VM",
                "VM_part_rev1.SLDPRT", "equations2.txt",
                drawing: null, overlay: null),
        };

    public static bool TryGet(WedgeType wedgeType, out TemplateSet templates) =>
        Templates.TryGetValue(wedgeType, out templates!);

    /// <summary>Templates for the model phase only (part + equations).</summary>
    public static TemplateSet GetForModel(WedgeType wedgeType) =>
        TryGet(wedgeType, out var templates)
            ? templates
            : throw new InvalidOperationException(
                $"No model automation template is configured for wedge type '{wedgeType}'.");

    /// <summary>Templates for a full model + drawing run; requires a drawing template for <paramref name="drawingType"/>.</summary>
    public static bool TryGetForDrawing(
        WedgeType wedgeType,
        DrawingType drawingType,
        out TemplateSet templates,
        out string drawingPath)
    {
        drawingPath = string.Empty;

        if (!TryGet(wedgeType, out templates))
            return false;

        var path = templates.DrawingPathFor(drawingType);
        if (path is null)
            return false;

        drawingPath = path;
        return true;
    }

    private static TemplateSet Set(
        string folder,
        string part,
        string equations,
        string? drawing,
        string? overlay)
    {
        var dir = Path.Combine(Root, folder);

        return new TemplateSet(
            Path.Combine(dir, part),
            Path.Combine(dir, equations),
            drawing is null ? null : Path.Combine(dir, drawing),
            overlay is null ? null : Path.Combine(dir, overlay));
    }
}

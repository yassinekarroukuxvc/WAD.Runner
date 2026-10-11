using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace WAD.Runner.DrawingAutomation.Overlay;

public sealed class OverlayLineFormatProfile
{
    public string SourceDrawing { get; init; } = "";
    public double ReferenceSheetWidthUnits { get; init; }
    public double[] HiddenPattern { get; init; } = Array.Empty<double>();
    public double GlobalLinetypeScale { get; init; }
    public double HiddenEntityScale { get; init; }
    public double ToleranceEntityScale { get; init; }
    public double FlEntityScale { get; init; }
    public double CalibrationEntityScale { get; init; }
    public int[] OutlineRgb { get; init; } = Array.Empty<int>();
    public int[] HiddenRgb { get; init; } = Array.Empty<int>();
    public int[] AnnotationRgb { get; init; } = Array.Empty<int>();
    public int[] CalibrationRgb { get; init; } = Array.Empty<int>();
    public int[] FrameRgb { get; init; } = Array.Empty<int>();
    public Dictionary<string, string> LayerRoles { get; init; } = new();
    public double ReferenceSheetHeightUnits { get; init; }
    public OverlayReferenceLayout? ReferenceLayout { get; init; }

    public static OverlayLineFormatProfile Load(string path)
    {
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, path);
        var profile = JsonSerializer.Deserialize<OverlayLineFormatProfile>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Invalid overlay format profile: {path}");
        if (!(profile.ReferenceSheetWidthUnits > 0) || !(profile.GlobalLinetypeScale > 0) ||
            profile.HiddenPattern.Length < 2 || profile.HiddenPattern.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException($"Invalid reference dash pattern: {path}");
        foreach (var rgb in new[] { profile.OutlineRgb, profile.HiddenRgb, profile.AnnotationRgb, profile.CalibrationRgb, profile.FrameRgb })
            if (rgb.Length != 3 || rgb.Any(v => v < 0 || v > 255)) throw new InvalidDataException($"Invalid reference RGB: {path}");
        foreach (var scale in new[] { profile.HiddenEntityScale, profile.ToleranceEntityScale, profile.FlEntityScale, profile.CalibrationEntityScale })
            if (!(scale > 0) || !double.IsFinite(scale)) throw new InvalidDataException($"Invalid reference line scale: {path}");
        return profile;
    }

    /// <summary>Maps the reference DWG's frame (origin at its lower-left corner) onto the sheet's frame, in meters.</summary>
    public (double X, double Y, double Scale) ToSheet(double[] frame, double x, double y)
    {
        if (!(ReferenceSheetHeightUnits > 0)) throw new InvalidDataException("Missing referenceSheetHeightUnits.");
        double sx = (frame[2] - frame[0]) / ReferenceSheetWidthUnits, sy = (frame[3] - frame[1]) / ReferenceSheetHeightUnits;
        return (frame[0] + x * sx, frame[1] + y * sy, sx);
    }

    public static int Color(int[] rgb) => rgb[0] | rgb[1] << 8 | rgb[2] << 16;
    public int RoleColor(string role) => Color(role switch
    {
        "Outline" => OutlineRgb, "Hidden" => HiddenRgb, "Frame" => FrameRgb,
        "Annotation" => AnnotationRgb, "Calibration" => CalibrationRgb,
        _ => throw new InvalidDataException($"Unknown overlay line role '{role}'.")
    });

    public static double MetersPerUnit(ModelDoc2 model)
    {
        var units = (Array)model.GetUnits();
        return (swLengthUnit_e)Convert.ToInt32(units.GetValue(0)) switch
        {
            swLengthUnit_e.swMM => 0.001, swLengthUnit_e.swCM => 0.01,
            swLengthUnit_e.swMETER => 1, swLengthUnit_e.swINCHES => 0.0254,
            swLengthUnit_e.swFEET => 0.3048,
            _ => throw new InvalidOperationException("Unsupported document units for reference overlay styles.")
        };
    }

    public Dictionary<string, string> RegisterStyles(ModelDoc2 model, DrawingDoc drawing)
    {
        var sheet = (Sheet)drawing.GetCurrentSheet();
        double width = 0, height = 0;
        sheet.GetSize(ref width, ref height);
        double metersPerUnit = MetersPerUnit(model);
        var result = new Dictionary<string, string>();
        foreach (var (role, scale) in new[] { ("Hidden", HiddenEntityScale), ("Tolerance", ToleranceEntityScale),
            ("FL", FlEntityScale), ("Calibration", CalibrationEntityScale) })
        {
            string name = "WAD_REFERENCE_" + role.ToUpperInvariant();
            // SW displays line-style length/spacing at 1/5 of its definition, in document units.
            double factor = GlobalLinetypeScale * scale * width / ReferenceSheetWidthUnits / metersPerUnit * 5;
            string definition = "A," + string.Join(",", HiddenPattern.Select(v => (v * factor).ToString("G17", CultureInfo.InvariantCulture)));
            if (!drawing.AddLineStyle(name, definition)) throw new InvalidOperationException($"Cannot register reference line style '{name}'.");
            result[role] = name;
        }
        return result;
    }
}

public sealed class OverlayReferenceLayout
{
    public string FrameLayer { get; init; } = "";
    /// <summary>Size of the description/coining blocks relative to the reference, scaled toward the frame's lower-left corner.</summary>
    public double BlockScale { get; init; } = 1;
    /// <summary>Monospac821 BT size correction to match AutoCAD's monotxt (the template maps their heights 1:1).</summary>
    public double MonoTextHeightScale { get; init; } = 1;
    public double MonoTextWidthScale { get; init; } = 1;
    /// <summary>Height of a left-justified note's anchor above its cap top, as a fraction of CharHeight.</summary>
    public double MonoTextAnchorAboveCap { get; init; }
    public OverlayReferenceBlock? DescriptionBox { get; init; }
    public OverlayReferenceBlock? CoiningBox { get; init; }
    public OverlayReferenceCalibrationNote? CalibrationNote { get; init; }
}

/// <summary>A template sketch block identified by one of its notes; box = reference x0, y0, x1, y1.</summary>
public sealed class OverlayReferenceBlock
{
    public string NoteText { get; init; } = "";
    public double[] Box { get; init; } = Array.Empty<double>();
    public double BorderWidthUnits { get; init; }
    /// <summary>The block's value note sits this far right of / below its label, as the reference's second text line.</summary>
    public double TextHeightUnits { get; init; }
    /// <summary>Reference x of each value note, keyed by the custom property it shows.</summary>
    public Dictionary<string, double> ValueX { get; init; } = new();
    public int SecondLineIndentCells { get; init; }
    public double SecondLinePitchUnits { get; init; }
}

/// <summary>Calibration label placement as fractions of the calibration box side.</summary>
public sealed class OverlayReferenceCalibrationNote
{
    public string UnitSuffix { get; init; } = "";
    public string Font { get; init; } = "";
    public double WidthFactor { get; init; }
    public double HeightFraction { get; init; }
    /// <summary>Right end of the reference label, so values longer than '700um' stay inside the box.</summary>
    public double RightFraction { get; init; }
    public double YFraction { get; init; }
}

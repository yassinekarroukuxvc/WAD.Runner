using System;

using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Planning.Rules;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules._4516;

/// <summary>
/// 4516 engraving (template engraving_sketch, text on the TDF flat): X1 = distance from the top of
/// TL to the box, X2 = box length along TL, X3 = distance from the tool axis to the line the text
/// sits on (the glyphs grow toward the axis). The flat face is the chord where the TDF flat cuts
/// the TD circle; the font is the largest that fits that chord (with margin) and the box length, and
/// X3 = font / 2 centres the text on the face. The box is fitted to the straight shank of the
/// display model, above the tip taper.
/// </summary>
public static class _4516EngravingLayout
{
    public const string SketchName = "engraving_sketch";

    private const double TopOffsetMm = 0.254;          // X1, 0.01 in
    private const double TemplateBoxLengthMm = 17.78;  // X2 in the template, 0.7 in
    private const double FaceFill = 0.85;               // text height / flat-face width
    private const double TemplateCharHeightMm = 1.1369;
    private const double TaperMarginMm = 0.3;
    // ISOCPEUR advance per character relative to its height: measured 0.71 on the engraved
    // "SPT-945-165-001" (5.2 mm at 0.487 mm); 0.75 keeps a small margin.
    private const double AdvancePerHeight = 0.75;

    public sealed record Layout(double X1Mm, double X2Mm, double X3Mm, double CharHeightMm, double FaceWidthMm,
        double TaperMm, double DisplayTlMm);

    public static Layout Compute(WedgeData wedge, string text)
    {
        double Mm(string key) =>
            wedge.TryGet(DimensionKey.From(key)) is { } d && d.Nominal.Unit == UnitKind.Millimeter ? (double)d.Nominal.Value : 0.0;
        double Deg(string key) =>
            wedge.TryGet(DimensionKey.From(key)) is { } d && d.Nominal.Unit == UnitKind.Degree ? (double)d.Nominal.Value : 0.0;

        var tl = _4516DisplayLength.TlMm(wedge);
        var td = Mm("TD");
        var w = Mm("W");
        var isa = Deg("ISA") > 0 ? Deg("ISA") : 20.0;

        // Tip taper: the ISA flanks go from W at the foot to TD on the shank.
        var taper = td > w ? (td - w) / (2.0 * Math.Tan(isa / 2.0 * Math.PI / 180.0)) : 0.0;

        // Flat face: chord of the TD circle at the TDF flat (distance TDF - R from the axis).
        var r = td / 2.0;
        var flatDistance = Mm("TDF") - r;
        var faceWidth = r > 0 && flatDistance > -r && flatDistance < r
            ? 2.0 * Math.Sqrt(r * r - flatDistance * flatDistance)
            : 2.0 * r;

        var x2 = Math.Clamp(tl - TopOffsetMm - taper - TaperMarginMm, 1.0, TemplateBoxLengthMm);
        var chars = Math.Max(1, text.Length);
        var height = Math.Min(TemplateCharHeightMm,
            Math.Min(faceWidth * FaceFill, x2 / (chars * AdvancePerHeight)));

        return new Layout(TopOffsetMm, x2, height / 2.0, height, faceWidth, taper, tl);
    }
}

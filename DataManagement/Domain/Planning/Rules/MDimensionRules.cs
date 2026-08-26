using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

internal static class MDimensionRules
{
    private const string Front = "Front";
    private const string Top = "Top";
    private const string Side = "Side";
    private const string Detail = "Detail";
    private const string Section = "Section";

    private const double SheetWidthMm = 276.0;
    private const double SheetHeightMm = 213.0;
    private const double EdgeMarginMm = 10.0;

    public static List<DimensionSpec> Build(LayoutContext ctx, PlannerDiagnostics diag)
    {
        var dims = new List<DimensionSpec>();

        var TL = LayoutMath.Dmm(ctx, "TL");
        var TD = LayoutMath.Dmm(ctx, "TD");

        if (TL <= 0)
            diag.Suspicious("PLN003", "TL <= 0 detected.");

        if (TD <= 0)
            diag.Suspicious("PLN003", "TD <= 0 detected.");

        switch (ctx.Drawing.DrawingType)
        {
            case DrawingType.Production:
            case DrawingType.Customer:
                AddProductionCustomer(ctx, diag, dims);
                break;

            case DrawingType.Overlay:
                AddOverlayBaseline(ctx, diag, dims);
                break;

            default:
                diag.Suspicious("PLN000", $"Unhandled DrawingType: {ctx.Drawing.DrawingType}");
                break;
        }

        return dims;
    }

    private static void AddProductionCustomer(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
    {
        var F = LayoutMath.View(ctx, Front);
        var T = LayoutMath.View(ctx, Top);
        var S = LayoutMath.View(ctx, Side);
        var D = LayoutMath.View(ctx, Detail);
        var Sec = LayoutMath.View(ctx, Section);

        var fsv = LayoutMath.Scale(ctx, Front);
        var tsv = LayoutMath.Scale(ctx, Top);
        var ssv = LayoutMath.Scale(ctx, Side);
        var dsv = LayoutMath.Scale(ctx, Detail);
        var scv = LayoutMath.Scale(ctx, Section);

        var TL = LayoutMath.Dmm(ctx, "TL");
        var TD = LayoutMath.Dmm(ctx, "TD");

        var LFront = TL * fsv;
        var LSide = TL * fsv;

        const double detailLower = 80.0;
        var detailBreak = GetBreakline(ctx, Detail, defaultMm: 50.0);
        var bandMidY = D[1] - (detailBreak + detailLower) / 2.0;

        AddFront(ctx, diag, outList, F, fsv, TL, TD, LFront);
        AddTop(ctx, diag, outList, T, tsv, TD);
        AddDetail(ctx, diag, outList, D, dsv, bandMidY);
        AddSide(ctx, diag, outList, S, ssv, TD, LSide);
        AddSection(ctx, diag, outList, Sec, scv, bandMidY);
    }

    private static void AddFront(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] F,
        double fsv,
        double TL,
        double TD,
        double LFront)
    {
        PlaceDim(ctx, diag, outList, "TL", Front, DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 13.5,
            F[1]);

        var VR = LayoutMath.Dmm(ctx, "VR");

        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 5.0,
            F[1] - LFront / 2.0 + VR / 2.0 * fsv);
    }

    private static void AddTop(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] T,
        double tsv,
        double TD)
    {
        _ = tsv;
        _ = TD;

        PlaceDim(ctx, diag, outList, "TD", Top, DimAxis.Vertical, T[0] + 5.0, T[1] - 5.0);
        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal, T[0], T[1] + 5.0);
    }

    private static void AddDetail(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] D,
        double dsv,
        double bandMidY)
    {
        var W = LayoutMath.Dmm(ctx, "W");
        var GD = LayoutMath.Dmm(ctx, "GD");
        var VR = LayoutMath.Dmm(ctx, "VR");
        var VW = LayoutMath.Dmm(ctx, "VW");
        var CD = LayoutMath.Dmm(ctx, "CD");
        var VRR = LayoutMath.Dmm(ctx, "VRR");

        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal, D[0], D[1]);
        PlaceDim(ctx, diag, outList, "VRA", Detail, DimAxis.Horizontal, D[0], bandMidY + 5.0);
        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal, D[0], bandMidY - 15.0);
        PlaceDim(ctx, diag, outList, "B", Detail, DimAxis.Horizontal, D[0], bandMidY - 3.0);
        PlaceDim(ctx, diag, outList, "GO", Detail, DimAxis.Horizontal, D[0], bandMidY - 3.0);
        PlaceDim(ctx, diag, outList, "CL", Detail, DimAxis.Horizontal, D[0], bandMidY - 3.0);
        PlaceDim(ctx, diag, outList, "W_NOM", Detail, DimAxis.Horizontal, D[0], bandMidY - 6.0);
        PlaceDim(ctx, diag, outList, "VW", Detail, DimAxis.Horizontal, D[0], bandMidY - 9.0);

        PlaceDim(ctx, diag, outList, "GD", Detail, DimAxis.Vertical,
            D[0] + W / 2.0 * dsv + 10.0,
            bandMidY + dsv * GD / 2.0);

        PlaceDim(ctx, diag, outList, "GD_G", Detail, DimAxis.Vertical,
            D[0] + W / 2.0 * dsv + 10.0,
            bandMidY + dsv * GD / 2.0);

        PlaceDim(ctx, diag, outList, "CD_NOM", Detail, DimAxis.Horizontal,
            D[0] + W / 2.0 * dsv + 5.0,
            bandMidY + dsv * CD / 2.0);
    }

    private static void AddSide(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] S,
        double ssv,
        double TD,
        double LSide)
    {
        var VBL = LayoutMath.Dmm(ctx, "VBL");

        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal, S[0] + 5.0, S[1]);
        PlaceDim(ctx, diag, outList, "BA_VBL", Side, DimAxis.Horizontal, S[0] + 5.0, S[1]);

        PlaceDim(ctx, diag, outList, "VBL", Side, DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + 4.0,
            S[1] - LSide / 2.0 + VBL / 2.0 * ssv);
    }

    private static void AddSection(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] Sec,
        double scv,
        double bandMidY)
    {
        var FL = LayoutMath.Dmm(ctx, "FL");
        var TD = LayoutMath.Dmm(ctx, "TD");
        var TDF = LayoutMath.Dmm(ctx, "TDF");
        var T = LayoutMath.Dmm(ctx, "T");
        var FD = LayoutMath.Dmm(ctx, "FD");
        var G = LayoutMath.Dmm(ctx, "G");
        var ERD = LayoutMath.Dmm(ctx, "ERD");
        var CGD = LayoutMath.Dmm(ctx, "CGD");
        var FR = LayoutMath.Dmm(ctx, "FR");
        var F = LayoutMath.Dmm(ctx, "F");
        var ERL = LayoutMath.Dmm(ctx, "ERL");
        var HA = LayoutMath.Ddeg(ctx, "HA");
        var RA = LayoutMath.Ddeg(ctx, "RA");
        var RA2 = LayoutMath.Ddeg(ctx, "RA2");

        PlaceDim(ctx, diag, outList, "FL", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL / 2 * scv, bandMidY - 15);

        PlaceDim(ctx, diag, outList, "BF", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FR + FD / 2 * scv, bandMidY - 3);

        PlaceDim(ctx, diag, outList, "G", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + G / 2 * scv, bandMidY - 12);

        PlaceDim(ctx, diag, outList, "FR", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY - 3);

        PlaceDim(ctx, diag, outList, "BR", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + 10, bandMidY - 3);

        PlaceDim(ctx, diag, outList, "FRO", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY);

        PlaceDim(ctx, diag, outList, "ERL", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + ERL / 2 * scv, bandMidY - 21);

        PlaceDim(ctx, diag, outList, "FD", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2.0) * scv + (FD / 2.0) * scv, bandMidY - 18.0);

        PlaceDim(ctx, diag, outList, "T", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T / 2 * scv, bandMidY - 24);

        PlaceDim(ctx, diag, outList, "ERD", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 10, bandMidY + ERD * scv / 2);

        PlaceDim(ctx, diag, outList, "H", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv,
            bandMidY + ((T - FD) * scv) * Math.Tan(HA * (Math.PI / 180.0)));

        PlaceDim(ctx, diag, outList, "RA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv,
            bandMidY + (((T - FD) * scv) * Math.Tan(RA * (Math.PI / 180.0))) / 2);

        PlaceDim(ctx, diag, outList, "CA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + ERL / 2 * scv, bandMidY + ERD / 2 * scv);

        PlaceDim(ctx, diag, outList, "FNA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 25,
            bandMidY + ((T - FD) * scv) * Math.Tan(HA * (Math.PI / 180.0)) + 10);

        PlaceDim(ctx, diag, outList, "HA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 20, bandMidY + 10);

        PlaceDim(ctx, diag, outList, "RA2", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 20,
            bandMidY + (((T - FD) * scv) * Math.Tan((RA + RA2) * (Math.PI / 180.0))) / 2);

        PlaceDim(ctx, diag, outList, "F", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FR * scv + F / 2 * scv, bandMidY - 9);

        PlaceDim(ctx, diag, outList, "CBRL", Section, DimAxis.Horizontal, 180, bandMidY - 6);

        PlaceDim(ctx, diag, outList, "CGD", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY + CGD / 2 * scv);

        PlaceDim(ctx, diag, outList, "CGR", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv, bandMidY + 10);

        PlaceDim(ctx, diag, outList, "CBRA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 23, bandMidY + 10);
    }

    private static void AddOverlayBaseline(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
    {
        _ = LayoutMath.View(ctx, Front);
        _ = LayoutMath.View(ctx, Top);
        _ = LayoutMath.View(ctx, Side);
        _ = LayoutMath.View(ctx, Section);

        const double fstScale = 2.0 / 3.0;
        var TD = LayoutMath.Dmm(ctx, "TD");

        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal, 152.4, 60.0);
        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal, 106.68, 60.0);

        PlaceDim(ctx, diag, outList, "VW", Front, DimAxis.Horizontal, 5.0, 8.0);
        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal, 18.0, 14.0);

        PlaceDim(ctx, diag, outList, "E", Side, DimAxis.Horizontal, 75.0, 3.0);
        PlaceDim(ctx, diag, outList, "X", Side, DimAxis.Horizontal, 76.2, 14.732);
        PlaceDim(ctx, diag, outList, "FX", Side, DimAxis.Horizontal, 76.2, 14.732);

        PlaceDim(ctx, diag, outList, "FA", Side, DimAxis.Horizontal,
            137.08,
            8.3566 + fstScale * TD / 2.0 + 4.0);

        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal,
            137.08,
            8.3566 - fstScale * TD / 2.0 - 4.0);
    }

    private static double GetBreakline(LayoutContext ctx, string view, double defaultMm)
    {
        if (!ctx.Drawing.Views.TryGetValue(view, out var drawingView) || drawingView is null)
            return defaultMm;

        if (drawingView.Params is not null)
        {
            if (drawingView.Params.TryGetValue("breakline_gap_mm", out var millimeters))
                return millimeters;

            if (drawingView.Params.TryGetValue("BreaklineGap", out var legacyMillimeters))
                return legacyMillimeters;
        }

        if (drawingView.Metadata is not null)
        {
            if (drawingView.Metadata.TryGetValue("breakline_gap_mm", out var value) &&
                double.TryParse(value, out var parsedValue))
            {
                return parsedValue;
            }

            if (drawingView.Metadata.TryGetValue("BreaklineGap", out var legacyValue) &&
                double.TryParse(legacyValue, out var parsedLegacyValue))
            {
                return parsedLegacyValue;
            }
        }

        return defaultMm;
    }

    private static void PlaceDim(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        string key,
        string view,
        DimAxis axis,
        double x,
        double y)
    {
        if (!ctx.Drawing.Views.ContainsKey(view))
        {
            diag.MissingView(view);
            return;
        }

        var position = ClampToSheet(diag, view, key, x, y);

        if (!ctx.TryGetDim(key, out var dimension))
        {
            diag.MissingDimension(key);

            outList.Add(new DimensionSpec
            {
                Id = $"{view}:{key}",
                View = view,
                Key = new DimensionKey(key),
                PositionMm = position,
                Axis = axis,
                Nominal = Quantity.MmOf(0.01m),
                Tol = default,
                Comment = null,
                Style = DimStyle.None
            });

            return;
        }

        var style = DimStyle.None;

        if (IsRef(dimension.Comment))
            style |= DimStyle.Reference;

        if (IsMin(dimension.Comment))
            style |= DimStyle.Min;

        outList.Add(new DimensionSpec
        {
            Id = $"{view}:{key}",
            View = view,
            Key = dimension.Key,
            PositionMm = position,
            Axis = axis,
            Nominal = dimension.Nominal,
            Tol = dimension.Tol,
            Comment = dimension.Comment,
            Style = style
        });
    }

    private static bool IsRef(string? comment)
        => !string.IsNullOrWhiteSpace(comment) &&
           System.Text.RegularExpressions.Regex.IsMatch(
               comment,
               @"\b(REF|REFERENCE)\b",
               System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsMin(string? comment)
        => !string.IsNullOrWhiteSpace(comment) &&
           System.Text.RegularExpressions.Regex.IsMatch(
               comment,
               @"\bMIN\b",
               System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static double[] ClampToSheet(
        PlannerDiagnostics diag,
        string view,
        string key,
        double x,
        double y)
    {
        const double minX = EdgeMarginMm;
        const double maxX = SheetWidthMm - EdgeMarginMm;
        const double minY = EdgeMarginMm;
        const double maxY = SheetHeightMm - EdgeMarginMm;

        var clampedX = x;
        var clampedY = y;

        if (double.IsNaN(clampedX) || double.IsInfinity(clampedX))
            clampedX = SheetWidthMm / 2.0;

        if (double.IsNaN(clampedY) || double.IsInfinity(clampedY))
            clampedY = SheetHeightMm / 2.0;

        clampedX = Math.Clamp(clampedX, minX, maxX);
        clampedY = Math.Clamp(clampedY, minY, maxY);

        if (clampedX != x || clampedY != y)
        {
            diag.Suspicious(
                "PLN010",
                $"{view}:{key} position ({x:F1},{y:F1}) was outside sheet bounds " +
                $"({SheetWidthMm}x{SheetHeightMm} mm) - clamped to ({clampedX:F1},{clampedY:F1}).");
        }

        return new[] { clampedX, clampedY };
    }
}

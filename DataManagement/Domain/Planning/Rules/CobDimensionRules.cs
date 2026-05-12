using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

internal static class CobDimensionRules
{
    private const string Front = "Front";
    private const string Top = "Top";
    private const string Side = "Side";
    private const string Detail = "Detail";
    private const string Section = "Section";

    public static List<DimensionSpec> Build(LayoutContext ctx, PlannerDiagnostics diag)
    {
        var dims = new List<DimensionSpec>();

        var TL = LayoutMath.Dmm(ctx, "TL");
        var TD = LayoutMath.Dmm(ctx, "TD");
        if (TL <= 0) diag.Suspicious("PLN003", "TL <= 0 detected.");
        if (TD <= 0) diag.Suspicious("PLN003", "TD <= 0 detected.");

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
        var L_front = LayoutMath.WedgeLength(ctx, TL, fsv);
        var L_side = LayoutMath.WedgeLength(ctx, TL, ssv);

        double detailLower = 60.0;
        double detailBreak = GetBreakline(ctx, Detail, defaultMm: 50.0);
        var bandMidY = D[1] - (detailBreak + detailLower) / 2.0;

        AddFront(ctx, diag, outList, F, fsv, TL, TD, L_front);
        AddTop(ctx, diag, outList, T, tsv, TD);
        AddDetail(ctx, diag, outList, D, dsv, bandMidY);
        AddSide(ctx, diag, outList, S, ssv, TD, L_side);
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
        double L_front)
    {
        PlaceDim(ctx, diag, outList, "TL", Front, DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 13.5, F[1]);

        var bias = (0.05 * TL * fsv + GetBreakline(ctx, Front, 2.0) * fsv + 0.02 * TL) / 2.0;
        PlaceDim(ctx, diag, outList, "K", Front, DimAxis.Horizontal,
            F[0] + fsv * TD / 2.0 + 12.0, F[1] + L_front / 2.0 - bias);

        var VR = LayoutMath.Dmm(ctx, "VR");
        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 5, F[1] - L_front / 2.0 + VR / 2 * fsv);
    }

    private static void AddTop(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] T,
        double tsv,
        double TD)
    {
        var TDF = LayoutMath.Dmm(ctx, "TDF");

        PlaceDim(ctx, diag, outList, "TD", Top, DimAxis.Vertical,
            55, 172);

        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal,
            45, 197);
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
        var TD = LayoutMath.Dmm(ctx, "TD");
        var VR = LayoutMath.Dmm(ctx, "VR");
        var VW = LayoutMath.Dmm(ctx, "VW");
        var GR = LayoutMath.Dmm(ctx, "GR");
        var CD = LayoutMath.Dmm(ctx, "CD");
        var CR = LayoutMath.Dmm(ctx, "CR");

        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal,
            D[0], D[1]);

        PlaceDim(ctx, diag, outList, "VRA", Detail, DimAxis.Horizontal,
            D[0], D[1] - 5);

        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 30);

        PlaceDim(ctx, diag, outList, "B", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 10);

        PlaceDim(ctx, diag, outList, "W", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 20);

        PlaceDim(ctx, diag, outList, "W2", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 15);

        PlaceDim(ctx, diag, outList, "VW", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 25);

        PlaceDim(ctx, diag, outList, "GD", Detail, DimAxis.Vertical,
            D[0] + (W / 2.0 * dsv) + 10.0, bandMidY + dsv * GD / 2.0);

        PlaceDim(ctx, diag, outList, "GR", Detail, DimAxis.Horizontal,
            D[0] - (W / 2.0 * dsv) - 10.0, bandMidY + 20);

        PlaceDim(ctx, diag, outList, "VR", Detail, DimAxis.Horizontal,
            D[0] - (VW * dsv / 2) - 10, bandMidY + (VR / 2) * dsv);

        PlaceDim(ctx, diag, outList, "CD", Detail, DimAxis.Horizontal,
            D[0] + (W / 2.0 * dsv) + 5, bandMidY + dsv * CD / 2.0);

        PlaceDim(ctx, diag, outList, "CR", Detail, DimAxis.Horizontal,
            119, 132);
    }

    private static void AddSide(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] S,
        double ssv,
        double TD,
        double L_side)
    {
        var FAdeg = LayoutMath.TryDdeg(ctx, "FA");
        var BAdeg = LayoutMath.TryDdeg(ctx, "BA");

        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal,
            52,
            114);
        PlaceDim(ctx, diag, outList, "TL", Side, DimAxis.Horizontal,
            S[0] - ssv * TD / 2.0 - 7.5, S[1]);
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

        PlaceDim(ctx, diag, outList, "FL", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL / 2 * scv, bandMidY - 25);

        PlaceDim(ctx, diag, outList, "G", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + G / 2 * scv, bandMidY - 20);

        PlaceDim(ctx, diag, outList, "FR_C", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "FR_VG", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "FR_CG", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "FR_G", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "BR_C", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "BR_VG", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "BR_CG", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "BR_G", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + 10, bandMidY - 5);

        PlaceDim(ctx, diag, outList, "FRO", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY);

        PlaceDim(ctx, diag, outList, "ERL", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + ERL / 2 * scv, bandMidY - 35);

        PlaceDim(ctx, diag, outList, "FD", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2.0) * scv + (FD / 2.0) * scv, bandMidY - 30.0);

        PlaceDim(ctx, diag, outList, "T", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T / 2 * scv, bandMidY - 40);

        PlaceDim(ctx, diag, outList, "ERD", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 10, bandMidY + ERD * scv / 2);

        PlaceDim(ctx, diag, outList, "H", Section, DimAxis.Horizontal,
            240, 133);

        PlaceDim(ctx, diag, outList, "RA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv, bandMidY + (((T - FD) * scv) * Math.Tan(RA * (Math.PI / 180.0))) / 2);

        PlaceDim(ctx, diag, outList, "CA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FL * scv + ERL / 2 * scv, bandMidY + ERD / 2 * scv);

        PlaceDim(ctx, diag, outList, "FNA", Section, DimAxis.Horizontal,
            230, 155);

        PlaceDim(ctx, diag, outList, "HA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + 20, bandMidY + 10);

        PlaceDim(ctx, diag, outList, "RA2", Section, DimAxis.Horizontal,
            225, 180);

        PlaceDim(ctx, diag, outList, "F_C", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FR * scv + F / 2 * scv, bandMidY - 15);

        PlaceDim(ctx, diag, outList, "F_G", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FR * scv + F / 2 * scv, bandMidY - 15);

        PlaceDim(ctx, diag, outList, "F_VG", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FR * scv + F / 2 * scv, bandMidY - 15);

        PlaceDim(ctx, diag, outList, "CBRL", Section, DimAxis.Horizontal,
            180, bandMidY - 10);

        PlaceDim(ctx, diag, outList, "CGD", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv - 10, bandMidY + CGD / 2 * scv);

        PlaceDim(ctx, diag, outList, "CGR", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv, bandMidY + 10);
    }

    private static void AddOverlayBaseline(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
    {
        _ = LayoutMath.View(ctx, Front);
        _ = LayoutMath.View(ctx, Top);
        _ = LayoutMath.View(ctx, Side);
        _ = LayoutMath.View(ctx, Section);

        var FSTscale = 2.0 / 3.0;
        var TD = LayoutMath.Dmm(ctx, "TD");
        var W = LayoutMath.Dmm(ctx, "W");

        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal, 152.4, 60.0);
        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal, 106.68, 60.0);

        PlaceDim(ctx, diag, outList, "VW", Front, DimAxis.Horizontal, 5.0, 8.0);
        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal, 18.0, 14.0);

        PlaceDim(ctx, diag, outList, "E", Side, DimAxis.Horizontal, 75, 3.0);
        PlaceDim(ctx, diag, outList, "X", Side, DimAxis.Horizontal, 76.2, 14.732);
        PlaceDim(ctx, diag, outList, "FX", Side, DimAxis.Horizontal, 76.2, 14.732);

        var FAdeg = LayoutMath.TryDdeg(ctx, "FA");
        PlaceDim(ctx, diag, outList, "FA", Side, DimAxis.Horizontal,
            132.08 + 5, 8.3566 + FSTscale * TD / 2.0 + 4.0);

        var BAdeg = LayoutMath.TryDdeg(ctx, "BA");
        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal,
            132.08 + 5, 8.3566 - FSTscale * TD / 2.0 - 4.0);
    }

    private static double GetBreakline(LayoutContext ctx, string view, double defaultMm)
    {
        if (!ctx.Drawing.Views.TryGetValue(view, out var v) || v is null) return defaultMm;

        if (v.Params is not null)
        {
            if (v.Params.TryGetValue("breakline_gap_mm", out var mm)) return mm;
            if (v.Params.TryGetValue("BreaklineGap", out var mm2)) return mm2;
        }

        if (v.Metadata is not null)
        {
            if (v.Metadata.TryGetValue("breakline_gap_mm", out var s1) && double.TryParse(s1, out var p1)) return p1;
            if (v.Metadata.TryGetValue("BreaklineGap", out var s2) && double.TryParse(s2, out var p2)) return p2;
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

        if (!ctx.TryGetDim(key, out var d))
        {
            diag.MissingDimension(key);
            outList.Add(new DimensionSpec
            {
                Id = $"{view}:{key}",
                View = view,
                Key = new DimensionKey(key),
                PositionMm = new[] { x, y },
                Axis = axis,
                Nominal = Quantity.MmOf(0.01m),
                Tol = default,
                Comment = null,
                Style = DimStyle.None
            });
            return;
        }

        var style = DimStyle.None;
        if (IsRef(d.Comment)) style |= DimStyle.Reference;
        if (IsMin(d.Comment)) style |= DimStyle.Min;

        outList.Add(new DimensionSpec
        {
            Id = $"{view}:{key}",
            View = view,
            Key = d.Key,
            PositionMm = new[] { x, y },
            Axis = axis,
            Nominal = d.Nominal,
            Tol = d.Tol,
            Comment = d.Comment,
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
}

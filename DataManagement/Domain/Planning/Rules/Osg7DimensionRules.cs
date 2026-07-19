using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

internal static class Osg7DimensionRules
{
    private const string Front = "Front";
    private const string Top = "Top";
    private const string Side = "Side";
    private const string Detail = "Detail";
    private const string Section = "Section";

    // OSG7 always uses a fixed TL of 63.5 mm
    // for drawing dimension-position calculations.
    private const double Osg7TlMm = 63.5;

    public static List<DimensionSpec> Build(
        LayoutContext ctx,
        PlannerDiagnostics diag)
    {
        var dims = new List<DimensionSpec>();

        var TD = LayoutMath.Dmm(ctx, "TD");

        if (TD <= 0)
            diag.Suspicious(
                "PLN003",
                "TD <= 0 detected.");

        switch (ctx.Drawing.DrawingType)
        {
            case DrawingType.Production:
            case DrawingType.Customer:
                AddProductionCustomer(
                    ctx,
                    diag,
                    dims);
                break;

            case DrawingType.Overlay:
                AddOverlayBaseline(
                    ctx,
                    diag,
                    dims);
                break;

            default:
                diag.Suspicious(
                    "PLN000",
                    $"Unhandled DrawingType: {ctx.Drawing.DrawingType}");
                break;
        }

        return dims;
    }

    private static void AddProductionCustomer(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList)
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

        // OSG7 TL is always fixed at 63.5 mm.
        var TL = Osg7TlMm;

        var TD = LayoutMath.Dmm(ctx, "TD");

        var L_front =
            LayoutMath.WedgeLength(
                ctx,
                TL,
                fsv);

        var L_side =
            LayoutMath.WedgeLength(
                ctx,
                TL,
                ssv);

        const double detailLower = 40.0;

        var detailBreak =
            GetBreakline(
                ctx,
                Detail,
                defaultMm: 50.0);

        var bandMidY =
            D[1] -
            (detailBreak + detailLower) / 2.0;

        AddFront(
            ctx,
            diag,
            outList,
            F,
            fsv,
            TD,
            L_front);

        AddTop(
            ctx,
            diag,
            outList,
            T,
            tsv,
            TD);

        AddDetail(
            ctx,
            diag,
            outList,
            D,
            dsv,
            bandMidY);

        AddSide(
            ctx,
            diag,
            outList,
            S,
            ssv,
            TD,
            L_side);

        AddSection(
            ctx,
            diag,
            outList,
            Sec,
            scv);
    }

    private static void AddFront(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] F,
        double fsv,
        double TD,
        double L_front)
    {
        PlaceDim(
            ctx,
            diag,
            outList,
            "TL",
            Front,
            DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 10.5,
            F[1]);

        var VR = LayoutMath.Dmm(ctx, "VR");

        PlaceDim(
            ctx,
            diag,
            outList,
            "VR",
            Front,
            DimAxis.Horizontal,
            F[0] + fsv * TD / 2.0 + 5.0,
            F[1] - L_front / 2.0 + VR * fsv / 2.0);
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

        PlaceDim(
            ctx,
            diag,
            outList,
            "TD",
            Top,
            DimAxis.Vertical,
            T[0] + tsv * TDF / 2.0 + 5.0,
            T[1] - tsv * TD / 2.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "TDF",
            Top,
            DimAxis.Horizontal,
            T[0],
            T[1] + tsv * TD / 2.0 + 5.0);
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

        PlaceDim(
            ctx,
            diag,
            outList,
            "VRA",
            Detail,
            DimAxis.Horizontal,
            D[0] - 3.5,
            D[1]);

        PlaceDim(
            ctx,
            diag,
            outList,
            "ISA",
            Detail,
            DimAxis.Horizontal,
            D[0] + 10,
            D[1]);

        PlaceDim(
            ctx,
            diag,
            outList,
            "GA",
            Detail,
            DimAxis.Horizontal,
            D[0],
            bandMidY);

        PlaceDim(
            ctx,
            diag,
            outList,
            "B",
            Detail,
            DimAxis.Horizontal,
            D[0],
            bandMidY - 10.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "W",
            Detail,
            DimAxis.Horizontal,
            D[0],
            bandMidY - 15.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "VW",
            Detail,
            DimAxis.Horizontal,
            D[0],
            bandMidY - 12.5);

        PlaceDim(
            ctx,
            diag,
            outList,
            "GD",
            Detail,
            DimAxis.Vertical,
            D[0] - W / 2.0 * dsv - 20.0,
            bandMidY + dsv * GD / 2.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "GR",
            Detail,
            DimAxis.Horizontal,
            D[0] + 15.0,
            bandMidY + dsv * GD + 15.0);
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
        var X = LayoutMath.Dmm(ctx, "X");
        var FX = LayoutMath.Dmm(ctx, "FX");

        PlaceDim(
            ctx,
            diag,
            outList,
            "FA",
            Side,
            DimAxis.Horizontal,
            S[0] - ssv * TD / 2.0 - 4.0,
            S[1] +
            (FAdeg < 6.0 && !double.IsNaN(FAdeg)
                ? 70.0
                : 20.0));

        PlaceDim(
            ctx,
            diag,
            outList,
            "BA",
            Side,
            DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + 4.0,
            S[1] +
            (BAdeg < 6.0 && !double.IsNaN(BAdeg)
                ? 55.0
                : 15.0));

        PlaceDim(
            ctx,
            diag,
            outList,
            "X",
            Side,
            DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + X * ssv / 2.0,
            S[1] - L_side / 2.0 - 4.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "FX",
            Side,
            DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 - FX * ssv / 2.0,
            S[1] - L_side / 2.0 - 4.0);
    }

    private static void AddSection(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] Sec,
        double scv)
    {
        var FL = LayoutMath.Dmm(ctx, "FL");
        var TDF = LayoutMath.Dmm(ctx, "TDF");

        var X = LayoutMath.Dmm(ctx, "X");
        var F = LayoutMath.Dmm(ctx, "F");
        var FX = LayoutMath.Dmm(ctx, "FX");

        if (X == 0)
            X = TDF - (FX + FL);

        if (FX == 0)
            FX = TDF - (X + FL);

        PlaceDim(
            ctx,
            diag,
            outList,
            "F",
            Section,
            DimAxis.Horizontal,
            Sec[0],
            Sec[1] - 60.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "FL",
            Section,
            DimAxis.Horizontal,
            Sec[0],
            Sec[1] - 65.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "FR",
            Section,
            DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + (FX * scv) - 10,
            Sec[1] - 40.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "BR",
            Section,
            DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv +
            (FX * scv) +
            (FL * scv) +
            10,
            Sec[1] - 40.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "BRX",
            Section,
            DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv +
            (FX * scv) +
            (FL * scv),
            Sec[1] - 40.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "FRX",
            Section,
            DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv +
            (FX * scv) +
            (FL * scv) +
            10,
            Sec[1] - 40.0);
    }

    private static void AddOverlayBaseline(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList)
    {
        const double FSTscale = 2.0 / 3.0;

        var TD = LayoutMath.Dmm(ctx, "TD");

        PlaceDim(
            ctx,
            diag,
            outList,
            "ISA",
            Detail,
            DimAxis.Horizontal,
            152.4,
            60.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "GA",
            Detail,
            DimAxis.Horizontal,
            106.68,
            60.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "VW",
            Front,
            DimAxis.Horizontal,
            5.0,
            8.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "VR",
            Front,
            DimAxis.Horizontal,
            18.0,
            14.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "E",
            Side,
            DimAxis.Horizontal,
            75.0,
            3.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "X",
            Side,
            DimAxis.Horizontal,
            76.2,
            14.732);

        PlaceDim(
            ctx,
            diag,
            outList,
            "FX",
            Side,
            DimAxis.Horizontal,
            76.2,
            14.732);

        PlaceDim(
            ctx,
            diag,
            outList,
            "TDF",
            Top,
            DimAxis.Horizontal,
            152.4,
            13.462);

        PlaceDim(
            ctx,
            diag,
            outList,
            "FA",
            Side,
            DimAxis.Horizontal,
            132.08 + 5.0,
            8.3566 +
            FSTscale * TD / 2.0 +
            4.0);

        PlaceDim(
            ctx,
            diag,
            outList,
            "BA",
            Side,
            DimAxis.Horizontal,
            132.08 + 5.0,
            8.3566 -
            FSTscale * TD / 2.0 -
            4.0);
    }

    private static double GetBreakline(
        LayoutContext ctx,
        string view,
        double defaultMm)
    {
        if (!ctx.Drawing.Views.TryGetValue(
                view,
                out var v) ||
            v is null)
        {
            return defaultMm;
        }

        if (v.Params is not null)
        {
            if (v.Params.TryGetValue(
                    "breakline_gap_mm",
                    out var mm))
            {
                return mm;
            }

            if (v.Params.TryGetValue(
                    "BreaklineGap",
                    out var mm2))
            {
                return mm2;
            }
        }

        if (v.Metadata is not null)
        {
            if (v.Metadata.TryGetValue(
                    "breakline_gap_mm",
                    out var s1) &&
                double.TryParse(
                    s1,
                    out var p1))
            {
                return p1;
            }

            if (v.Metadata.TryGetValue(
                    "BreaklineGap",
                    out var s2) &&
                double.TryParse(
                    s2,
                    out var p2))
            {
                return p2;
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

        if (!ctx.TryGetDim(
                key,
                out var d))
        {
            diag.MissingDimension(key);

            outList.Add(
                new DimensionSpec
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

        if (IsRef(d.Comment))
            style |= DimStyle.Reference;

        if (IsMin(d.Comment))
            style |= DimStyle.Min;

        outList.Add(
            new DimensionSpec
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


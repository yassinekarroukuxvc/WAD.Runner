// Domain/Planning/Rules/CobDimensionRules.cs
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

/// <summary>
/// COB-specific dimension placement rules.
///
/// TEMPORARY:
/// - Uses the same placement logic as CKVD today,
///   but the rules LIVE HERE and are organized by view.
/// - Later, replace view blocks with true COB logic.
/// </summary>
internal static class CobDimensionRules
{
    private const string Front = "Front";
    private const string Top = "Top";
    private const string Side = "Side";
    private const string Detail = "Detail";
    private const string Section = "Section";

    public static List<DimensionSpec> Build(LayoutContext ctx, PlannerDiagnostics diag)
    {
        Logger.Info($"[Plan] Enter CobDimensionRules.Build (dtype={ctx.Drawing.DrawingType})");

        var dims = new List<DimensionSpec>();

        // Basic sanity
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

    // ============================================================
    // PRODUCTION / CUSTOMER  (organized by view)
    // ============================================================
    private static void AddProductionCustomer(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
    {
        // Views
        var F = LayoutMath.View(ctx, Front);
        var T = LayoutMath.View(ctx, Top);
        var S = LayoutMath.View(ctx, Side);
        var D = LayoutMath.View(ctx, Detail);
        var Sec = LayoutMath.View(ctx, Section);

        // Scales
        var fsv = LayoutMath.Scale(ctx, Front);
        var tsv = LayoutMath.Scale(ctx, Top);
        var ssv = LayoutMath.Scale(ctx, Side);
        var dsv = LayoutMath.Scale(ctx, Detail);
        var scv = LayoutMath.Scale(ctx, Section);

        Logger.Info($"[Plan] Scales → Front={fsv:0.###}, Side={ssv:0.###}, Top={tsv:0.###}, Detail={dsv:0.###}, Section={scv:0.###}");

        // Common values
        var TL = LayoutMath.Dmm(ctx, "TL");
        var TD = LayoutMath.Dmm(ctx, "TD");
        var L_front = LayoutMath.WedgeLength(ctx, TL, fsv);
        var L_side = LayoutMath.WedgeLength(ctx, TL, ssv);

        // Detail breakline defaults
        double detailLower = 40.0;
        double detailBreak = GetBreakline(ctx, Detail, defaultMm: 50.0);
        var bandMidY = D[1] - (detailBreak + detailLower) / 2.0;

        // ----- VIEW BLOCKS -----
        AddFront(ctx, diag, outList, F, fsv, TL, TD, L_front);
        AddTop(ctx, diag, outList, T, tsv, TD);
        AddDetail(ctx, diag, outList, D, dsv, bandMidY);
        AddSide(ctx, diag, outList, S, ssv, TD, L_side);
        AddSection(ctx, diag, outList, Sec, scv);
    }

    // ----------------- FRONT -----------------
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
    }

    // ----------------- TOP -----------------
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
            95, 178);

        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal,
            83,197);

    }

    // ----------------- DETAIL -----------------
    private static void AddDetail(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] D,
        double dsv,
        double bandMidY)
    {
        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal,
            D[0] + 3.5, D[1]);

        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal,
            D[0], bandMidY);

        PlaceDim(ctx, diag, outList, "B", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 10.0);

        PlaceDim(ctx, diag, outList, "W", Detail, DimAxis.Horizontal,
            121,145);

        var W = LayoutMath.Dmm(ctx, "W");
        var GD = LayoutMath.Dmm(ctx, "GD");

        PlaceDim(ctx, diag, outList, "GD", Detail, DimAxis.Vertical,
            D[0] - W / 2.0 * dsv - 20.0, bandMidY + dsv * GD / 2.0);

        PlaceDim(ctx, diag, outList, "GR", Detail, DimAxis.Horizontal,
            D[0] + 15.0, bandMidY + dsv * GD + 15.0);

        PlaceDim(ctx, diag, outList, "VR", Detail, DimAxis.Horizontal,
            108, 162);

        PlaceDim(ctx, diag, outList, "VW", Detail, DimAxis.Horizontal,
            121,147);

        PlaceDim(ctx, diag, outList, "W2", Detail, DimAxis.Horizontal,
            D[0] + 15.0, bandMidY + dsv * GD + 15.0);

        PlaceDim(ctx, diag, outList, "VRR", Detail, DimAxis.Horizontal,
            0, 0);

        PlaceDim(ctx, diag, outList, "CD", Detail, DimAxis.Horizontal,
            146,162);

        PlaceDim(ctx, diag, outList, "CR", Detail, DimAxis.Horizontal,
            119, 162);
        
        PlaceDim(ctx, diag, outList, "VRA", Detail, DimAxis.Horizontal,
            127, 177);

        
    }

    // ----------------- SIDE -----------------
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
            92,
            114);

    }

    // ----------------- SECTION -----------------
    private static void AddSection(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] Sec,
        double scv)
    {
        var FL = LayoutMath.Dmm(ctx, "FL");

        PlaceDim(ctx, diag, outList, "FL", Section, DimAxis.Horizontal, 186, 146);

        PlaceDim(ctx, diag, outList, "FR_C", Section, DimAxis.Horizontal,
            171, 152);

        PlaceDim(ctx, diag, outList, "FR_VG", Section, DimAxis.Horizontal,
            171, 152);

        PlaceDim(ctx, diag, outList, "FR_CG", Section, DimAxis.Horizontal,
            171, 152);

        PlaceDim(ctx, diag, outList, "FR_G", Section, DimAxis.Horizontal,
            171,152);

        PlaceDim(ctx, diag, outList, "BR_C", Section, DimAxis.Horizontal,
            195, 152);

        PlaceDim(ctx, diag, outList, "BR_VG", Section, DimAxis.Horizontal,
            195, 152);

        PlaceDim(ctx, diag, outList, "BR_CG", Section, DimAxis.Horizontal,
            195, 152);

        PlaceDim(ctx, diag, outList, "BR_G", Section, DimAxis.Horizontal,
            195,152);

        PlaceDim(ctx, diag, outList, "FRO", Section, DimAxis.Horizontal,
            170,148 );

        PlaceDim(ctx, diag, outList, "ERL", Section, DimAxis.Horizontal,
            193, 143);

        PlaceDim(ctx, diag, outList, "FD", Section, DimAxis.Horizontal,
            175, 140);

        PlaceDim(ctx, diag, outList, "T", Section, DimAxis.Horizontal,
            195, 138);

        PlaceDim(ctx, diag, outList, "ERD", Section, DimAxis.Horizontal,
            212, 156);

        PlaceDim(ctx, diag, outList, "H", Section, DimAxis.Horizontal,
            220, 163);

        PlaceDim(ctx, diag, outList, "RA", Section, DimAxis.Horizontal,
            228, 157);

        PlaceDim(ctx, diag, outList, "CA", Section, DimAxis.Horizontal,
            196, 158);
        PlaceDim(ctx, diag, outList, "FNA", Section, DimAxis.Horizontal,
            232, 177);

        PlaceDim(ctx, diag, outList, "HA", Section, DimAxis.Horizontal,
            233, 165);
        PlaceDim(ctx, diag, outList, "RA2", Section, DimAxis.Horizontal,
            253, 175);


    }

    // ============================================================
    // OVERLAY (organized by view)
    // ============================================================
    private static void AddOverlayBaseline(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
    {
        // Views (kept for symmetry / future extension)
        _ = LayoutMath.View(ctx, Front);
        _ = LayoutMath.View(ctx, Top);
        _ = LayoutMath.View(ctx, Side);
        _ = LayoutMath.View(ctx, Section);

        // Baseline uses constant 2/3 for Front/Top/Side
        var FSTscale = 2.0 / 3.0;
        var TD = LayoutMath.Dmm(ctx, "TD");

        // DETAIL
        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal, 152.4, 60.0);
        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal, 106.68, 60.0);

        // FRONT
        PlaceDim(ctx, diag, outList, "VW", Front, DimAxis.Horizontal, 5.0, 8.0);
        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal, 18.0, 14.0);

        // SIDE
        PlaceDim(ctx, diag, outList, "E", Side, DimAxis.Horizontal, 75, 3.0);
        PlaceDim(ctx, diag, outList, "X", Side, DimAxis.Horizontal, 76.2, 14.732);
        PlaceDim(ctx, diag, outList, "FX", Side, DimAxis.Horizontal, 76.2, 14.732);

        // TOP
        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal, 152.4, 13.462);

        // Angles (SIDE)
        var FAdeg = LayoutMath.TryDdeg(ctx, "FA");
        PlaceDim(ctx, diag, outList, "FA", Side, DimAxis.Horizontal,
            132.08 + 5, 8.3566 + FSTscale * TD / 2.0 + 4.0);

        var BAdeg = LayoutMath.TryDdeg(ctx, "BA");
        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal,
            132.08 + 5, 8.3566 - FSTscale * TD / 2.0 - 4.0);
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private static double GetBreakline(LayoutContext ctx, string view, double defaultMm)
    {
        if (!ctx.Drawing.Views.TryGetValue(view, out var v) || v is null) return defaultMm;

        // Params (numeric doubles)
        if (v.Params is not null)
        {
            if (v.Params.TryGetValue("breakline_gap_mm", out var mm)) return mm;
            if (v.Params.TryGetValue("BreaklineGap", out var mm2)) return mm2;
        }

        // Metadata (string values)
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
            Logger.Warn($"[Plan.Drop] Missing view='{view}' for key='{key}'.");
            return;
        }

        // If missing in WedgeData: still plan as "move-only"
        if (!ctx.TryGetDim(key, out var d))
        {
            diag.MissingDimension(key);
            Logger.Warn($"[Plan.AddMissing] Dim key='{key}' missing in WedgeData. Planning as MOVE-ONLY (view='{view}').");

            outList.Add(new DimensionSpec
            {
                Id = $"{view}:{key}",
                View = view,
                Key = new DimensionKey(key),
                PositionMm = new[] { x, y },
                Axis = axis,

                // placeholders (not used by repositioning)
                Nominal = Quantity.MmOf(0.01m),
                Tol = default,         // <-- if this doesn't compile, replace with Tolerance.None / Tolerance.Zero / new Tolerance(...)
                Comment = null,
                Style = DimStyle.None
            });

            Logger.Info($"[Plan.AddMissing] {view}:{key} pos=({x:0.##},{y:0.##})");
            return;
        }

        // Normal path (dimension exists)
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

        Logger.Info($"[Plan.Add] {view}:{key} pos=({x:0.##},{y:0.##})");
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
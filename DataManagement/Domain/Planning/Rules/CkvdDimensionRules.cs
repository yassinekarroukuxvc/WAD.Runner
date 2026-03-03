// Domain/Planning/Rules/CkvdDimensionRules.cs
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

/// <summary>
/// CKVD-specific dimension placement rules.
/// Coverage: FG Production/Customer and baseline Overlay.
/// Matches legacy positions while staying SolidWorks-agnostic.
/// </summary>
internal static class CkvdDimensionRules
{
    private const string Front = "Front";
    private const string Top = "Top";
    private const string Side = "Side";
    private const string Detail = "Detail";
    private const string Section = "Section";

    public static List<DimensionSpec> Build(LayoutContext ctx, PlannerDiagnostics diag)
    {
        Logger.Info($"[Plan] Enter CkvdDimensionRules.Build (dtype={ctx.Drawing.DrawingType})");
        var dims = new List<DimensionSpec>();

        var TL = LayoutMath.Dmm(ctx, "TL");
        var TD = LayoutMath.Dmm(ctx, "TD");
        if (TL <= 0) diag.Suspicious("PLN003", "TL <= 0 detected.");
        if (TD <= 0) diag.Suspicious("PLN003", "TD <= 0 detected.");

        switch (ctx.Drawing.DrawingType)
        {
            case DrawingType.Production:
            case DrawingType.Customer:
                AddFgProductionCustomer(ctx, diag, dims);
                break;

            case DrawingType.Overlay:
                AddFgOverlayBaseline(ctx, diag, dims);
                break;

            default:
                diag.Suspicious("PLN000", $"Unhandled DrawingType: {ctx.Drawing.DrawingType}");
                break;
        }

        return dims;
    }

    // ----------------- FG Production / Customer rules -----------------
    private static void AddFgProductionCustomer(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
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

        // Use your desired defaults: Detail breakline defaults to 50 mm if nothing is set
        double detailLower = 40.0;
        double detailBreak = GetBreakline(ctx, Detail, defaultMm: 50.0);

        // ----- FRONT -----
        PlaceDim(ctx, diag, outList, "TL", Front, DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 13.5, F[1]);

        PlaceDim(ctx, diag, outList, "VW", Front, DimAxis.Horizontal,
            F[0] - fsv * TD / 2.0 - 8.0, F[1] - L_front / 2.0 - 3.0);

        var VR = LayoutMath.Dmm(ctx, "VR");
        PlaceDim(ctx, diag, outList, "cleaned_VR", Front, DimAxis.Horizontal,
            F[0] + fsv * TD / 2.0 + 5.0, F[1] - L_front / 2.0 + VR * fsv / 2.0);

        // Front bias uses 2 mm default via GetBreakline on Front
        var bias = (0.05 * TL * fsv + GetBreakline(ctx, Front, 2.0) * fsv + 0.02 * TL) / 2.0;
        PlaceDim(ctx, diag, outList, "D2", Front, DimAxis.Horizontal,
            F[0] + fsv * TD / 2.0 + 12.0, F[1] + L_front / 2.0 - bias);

        // ----- TOP -----
        var TDF = LayoutMath.Dmm(ctx, "TDF");
        PlaceDim(ctx, diag, outList, "TD", Top, DimAxis.Vertical,
            T[0] + tsv * TDF / 2.0 + 25.0, T[1] - tsv * TD / 2.0);

        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal,
            T[0] + tsv * TDF / 2.0 + 20.0, T[1] + tsv * TD / 2.0 + 6.0);

        PlaceDim(ctx, diag, outList, "DatumFeature", Top, DimAxis.Horizontal,
            T[0] + tsv * TDF / 2.0 + 10.0, T[1] - tsv * TD / 2.0 - 10.0);

        // ----- DETAIL -----
        var bandMidY = D[1] - (detailBreak + detailLower) / 2.0;
        Logger.Info($"[Plan] Scales → Front={fsv:0.###}, Side={ssv:0.###}, Top={tsv:0.###}, Detail={dsv:0.###}, Section={scv:0.###}");

        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal,
            D[0] + 3.5, D[1]);

        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal,
            D[0], bandMidY);

        PlaceDim(ctx, diag, outList, "B", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 10.0);

        PlaceDim(ctx, diag, outList, "W", Detail, DimAxis.Horizontal,
            D[0], bandMidY - 15.0);

        var W = LayoutMath.Dmm(ctx, "W");
        var GD = LayoutMath.Dmm(ctx, "GD");

        PlaceDim(ctx, diag, outList, "GD", Detail, DimAxis.Vertical,
            D[0] - W / 2.0 * dsv - 20.0, bandMidY + dsv * GD / 2.0);

        PlaceDim(ctx, diag, outList, "D1", Detail, DimAxis.Vertical,
            D[0] - W / 2.0 * dsv - 20.0, bandMidY + dsv * GD / 2.0);

        PlaceDim(ctx, diag, outList, "GR", Detail, DimAxis.Horizontal,
            D[0] + 15.0, bandMidY + dsv * GD + 15.0);

        // ----- SIDE -----
        var FAdeg = LayoutMath.TryDdeg(ctx, "FA");
        var BAdeg = LayoutMath.TryDdeg(ctx, "BA");

        PlaceDim(ctx, diag, outList, "FA", Side, DimAxis.Horizontal,
            S[0] - ssv * TD / 2.0 - 4.0,
            S[1] + (FAdeg < 6.0 && !double.IsNaN(FAdeg) ? 70.0 : 20.0));

        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + 4.0,
            S[1] + (BAdeg < 6.0 && !double.IsNaN(BAdeg) ? 55.0 : 15.0));

        var E = LayoutMath.Dmm(ctx, "E");

        PlaceDim(ctx, diag, outList, "E", Side, DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + 10.0,
            S[1] - L_side / 2.0 + E * ssv / 2.0);

        PlaceDim(ctx, diag, outList, "X", Side, DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + 10.0,
            S[1] - L_side / 2.0 - 8.0);

        var TIP = LayoutMath.Dmm(ctx, "TIP");
        PlaceDim(ctx, diag, outList, "CRMET", Side, DimAxis.Horizontal,
            S[0] - ssv * TD / 2.0 - 10.0,
            S[1] - L_side / 2.0 - 8.0);

        PlaceDim(ctx, diag, outList, "TIP", Side, DimAxis.Horizontal,
            S[0] - ssv * TD / 2.0 - 3.0,
            S[1] - L_side / 2.0 + TIP * ssv / 2.0);

        // ----- SECTION -----
        var FL = LayoutMath.Dmm(ctx, "FL");
        var GDsec = GD;

        PlaceDim(ctx, diag, outList, "F", Section, DimAxis.Horizontal, Sec[0], Sec[1] - 40 - 20);
        PlaceDim(ctx, diag, outList, "FL", Section, DimAxis.Horizontal, Sec[0], Sec[1] - 40 - 25);

        PlaceDim(ctx, diag, outList, "FR", Section, DimAxis.Horizontal,
            Sec[0] + scv * FL / 2, Sec[1] - 40);

        PlaceDim(ctx, diag, outList, "BR", Section, DimAxis.Horizontal,
            Sec[0] - scv * FL / 2, Sec[1] - 40);
    }

    // ----------------- FG Overlay baseline rules -----------------
    private static void AddFgOverlayBaseline(LayoutContext ctx, PlannerDiagnostics diag, List<DimensionSpec> outList)
    {
        var F = LayoutMath.View(ctx, Front);
        var T = LayoutMath.View(ctx, Top);
        var S = LayoutMath.View(ctx, Side);
        var Sec = LayoutMath.View(ctx, Section);
        var FSTscale = 2.0 / 3.0;
        var fsv = FSTscale;
        var tsv = FSTscale;
        var ssv = FSTscale;
        var scv = LayoutMath.Scale(ctx, Section);

        var TL = LayoutMath.Dmm(ctx, "TL");
        var TD = LayoutMath.Dmm(ctx, "TD");
        var TDF = LayoutMath.Dmm(ctx, "TDF");
        var E = LayoutMath.Dmm(ctx, "E");
        var FL = LayoutMath.Dmm(ctx, "FL");
        var GD = LayoutMath.Dmm(ctx, "GD");

        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal,
            152.4, 60.0);

        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal,
            106.68, 60.0);

        PlaceDim(ctx, diag, outList, "VW", Front, DimAxis.Horizontal,
            5.0, 8.0);

        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal,
            18.0, 14.0);

        PlaceDim(ctx, diag, outList, "E", Side, DimAxis.Horizontal,
            75, 3.0);

        PlaceDim(ctx, diag, outList, "X", Side, DimAxis.Horizontal,
            76.2, 14.732);
        PlaceDim(ctx, diag, outList, "FX", Side, DimAxis.Horizontal,
           76.2, 14.732);

        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal,
            152.4, 13.462);

        var FAdeg = LayoutMath.TryDdeg(ctx, "FA");
        PlaceDim(ctx, diag, outList, "FA", Side, DimAxis.Horizontal,
            132.08 + 5, 8.3566 + FSTscale * TD / 2.0 + 4.0);

        var BAdeg = LayoutMath.TryDdeg(ctx, "BA");
        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal,
            132.08 + 5,
            /*!double.IsNaN(BAdeg) && BAdeg < 6.0 ? 8.3566 - FSTscale * TD / 2.0 + 4.0 :*/ 8.3566 - FSTscale * TD / 2.0 - 4.0);
    }

    // ----------------- helpers -----------------
    /// <summary>
    /// Reads breakline gap (mm) for a view. Accepted keys:
    /// Params: "breakline_gap_mm" (preferred), "BreaklineGap"
    /// Metadata: "breakline_gap_mm", "BreaklineGap"
    /// Falls back to <paramref name="defaultMm"/>.
    /// </summary>
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

        if (!ctx.TryGetDim(key, out var d))
        {
            diag.MissingDimension(key);
            Logger.Warn($"[Plan.Drop] Missing dim key='{key}' (view='{view}').");
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
           System.Text.RegularExpressions.Regex.IsMatch(comment, @"\b(REF|REFERENCE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsMin(string? comment)
        => !string.IsNullOrWhiteSpace(comment) &&
           System.Text.RegularExpressions.Regex.IsMatch(comment, @"\bMIN\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

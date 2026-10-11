using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

internal static class _4516DimensionRules
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

        //var TL = LayoutMath.Dmm(ctx, "TL");
        var TL = _4516DisplayLength.TlMm(ctx.Wedge);
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

        //var TL = LayoutMath.Dmm(ctx, "TL");
        var TL = _4516DisplayLength.TlMm(ctx.Wedge);
        var TD = LayoutMath.Dmm(ctx, "TD");

        var LFront = TL * fsv;
        var LSide = TL * fsv;

        var detailLower = ctx.RetainedHeightsMm.TryGetValue(Detail, out var detailHeight) ? detailHeight : 60.0;
        var detailBreak = GetBreakline(ctx, Detail, defaultMm: 50.0);
        var bandMidY = D[1] - (detailBreak + detailLower) / 2.0;

        AddFront(ctx, diag, outList, F, fsv, TL, TD, LFront);
        AddTop(ctx, diag, outList, T, tsv, TD);
        AddDetail(ctx, diag, outList, D, dsv, bandMidY, detailLower);
        AddSide(ctx, diag, outList, S, ssv, TD, LSide);
        var sectionHeight = ctx.RetainedHeightsMm.TryGetValue(Section, out var retainedHeight) ? retainedHeight : 60.0;
        var sectionBaseline = Sec[1] - (GetBreakline(ctx, Section, 50.0) + sectionHeight) / 2.0;
        AddSection(ctx, diag, outList, Sec, scv, sectionBaseline);
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
        // The model origin is above the visible top by half the shank diameter.
        // The template's shank grows upward with the display TL while the view origin stays near the
        // foot; the layout moves the view down by the extra length, so the top is (TL - 12) x scale
        // above where it would be for the 12 mm model.
        var extraFront = (TL - _4516DisplayLength.BaseTlMm) * fsv;
        var centerY = F[1] + extraFront - fsv * TD / 2.0 - LFront / 2.0;
        PlaceDim(ctx, diag, outList, "TL", Front, DimAxis.Horizontal,
            Math.Max(10.0, F[0] - fsv * TD / 2.0 - 13.5),
            centerY, centerText: false);

        PlaceDim(ctx, diag, outList, "D1", Front, DimAxis.Horizontal,
            Math.Max(10.0, F[0] - fsv * TD / 2.0 - 13.5),
            centerY, centerText: false);

        // VR runs from the bottom of the tool up to VR max: text on the right of the view,
        // halfway up that span (clamped to the displayed tool length).
        var VR = LayoutMath.Dmm(ctx, "VR");
        var bottomY = F[1] + extraFront - fsv * TD / 2.0 - LFront;
        var vrSpan = Math.Min(Math.Max(VR * fsv, 0.0), LFront);

        PlaceDim(ctx, diag, outList, "VR", Front, DimAxis.Horizontal,
            F[0] + fsv * TD / 2.0 + 4.0,
            bottomY + vrSpan / 2.0, centerText: false);
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

        var TDF = LayoutMath.Dmm(ctx, "TDF");

        PlaceDim(ctx, diag, outList, "TD", Top, DimAxis.Vertical, T[0] + (TDF / 2) * tsv + 7.0, T[1] - (TD / 2) * tsv - 5.0);
        PlaceDim(ctx, diag, outList, "TDF", Top, DimAxis.Horizontal, T[0], T[1] + 5.0 + (TD / 2) * tsv);
    }

    private static void AddDetail(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        List<DimensionSpec> outList,
        double[] D,
        double dsv,
        double bandMidY,
        double retainedHeight)
    {
        var W = LayoutMath.Dmm(ctx, "W");
        var GD = LayoutMath.Dmm(ctx, "GD");
        var VR = LayoutMath.Dmm(ctx, "VR");
        var VW = LayoutMath.Dmm(ctx, "VW");
        var CD = LayoutMath.Dmm(ctx, "CD");
        var VRR = LayoutMath.Dmm(ctx, "VRR");

        // Clear of the break-line symbol at the top of the foot view.
        // ISA above the top of the kept part (retained height above the foot); 72 mm at the usual 60.
        PlaceDim(ctx, diag, outList, "ISA", Detail, DimAxis.Horizontal, D[0], bandMidY + retainedHeight + 12.0);
        // VRA sits at the top, directly under ISA.
        PlaceDim(ctx, diag, outList, "VRA", Detail, DimAxis.Horizontal, D[0], bandMidY + retainedHeight + 4.0);
        // B, GA and W form a centered stack with equal 6 mm spacing.
        PlaceDim(ctx, diag, outList, "GA", Detail, DimAxis.Horizontal, D[0], bandMidY - 9.0, centerText: false);
        PlaceDim(ctx, diag, outList, "B", Detail, DimAxis.Horizontal, D[0], bandMidY - 3.0);
        // GO is narrower than its text: place the text outside the extension lines, left of the
        // foot (GD sits on the right), so it does not sit on the slot and W.
        PlaceDim(ctx, diag, outList, "GO", Detail, DimAxis.Horizontal,
            D[0] - W / 2.0 * dsv - 6.0,
            bandMidY - 3.0);
        PlaceDim(ctx, diag, outList, "CL", Detail, DimAxis.Horizontal, D[0], bandMidY - 3.0);
        PlaceDim(ctx, diag, outList, "W", Detail, DimAxis.Horizontal, D[0], bandMidY - 15.0);
        PlaceDim(ctx, diag, outList, "VW", Detail, DimAxis.Horizontal, D[0], bandMidY - 21.0);

        // GD is shorter than its text: put the text above the extension lines instead of between
        // the arrows.
        PlaceDim(ctx, diag, outList, "GD", Detail, DimAxis.Vertical,
            D[0] + W / 2.0 * dsv + 10.0,
            bandMidY + dsv * GD + 4.0);

        PlaceDim(ctx, diag, outList, "GD_G", Detail, DimAxis.Vertical,
            D[0] + W / 2.0 * dsv + 10.0,
            bandMidY + dsv * GD + 4.0);

        PlaceDim(ctx, diag, outList, "GR_G", Detail, DimAxis.Vertical,
            D[0] - W / 2.0 * dsv - 10.0,
            bandMidY + dsv * GD + 10, centerText: false);

        PlaceDim(ctx, diag, outList, "GR_VG", Detail, DimAxis.Vertical,
            D[0] - W / 2.0 * dsv - 10.0,
            bandMidY + dsv * GD + 10, centerText: false);

        PlaceDim(ctx, diag, outList, "CD", Detail, DimAxis.Horizontal,
            D[0] + W / 2.0 * dsv + 5.0,
            bandMidY + dsv * CD / 2.0 - 3);
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
        var TLside = _4516DisplayLength.TlMm(ctx.Wedge);
        var topY = S[1] + (TLside - _4516DisplayLength.BaseTlMm) * ssv - ssv * TD / 2.0;
        var bottomY = topY - LSide;
        var vblSpan = Math.Min(Math.Max(VBL * ssv, 0.0), LSide);

        // The angle text sits level with the visible top of the tool, just right of the slanted
        // arm (which leaves the vertical edge at the bottom, or at the top of VBL with SLB), so it
        // clears the view outline and the arc arrows.
        double AngleX(string key, double rise)
        {
            var deg = LayoutMath.Ddeg(ctx, key);
            if (deg <= 0) deg = LayoutMath.Ddeg(ctx, "BA");
            if (deg <= 0 || deg >= 45) deg = 10.0;
            return S[0] + rise * Math.Tan(deg * Math.PI / 180.0) + 4.5;
        }

        PlaceDim(ctx, diag, outList, "BA", Side, DimAxis.Horizontal, AngleX("BA", LSide), topY, centerText: false);
        PlaceDim(ctx, diag, outList, "D1", Side, DimAxis.Horizontal, AngleX("BA", LSide), topY, centerText: false);
        PlaceDim(ctx, diag, outList, "BA_VBL", Side, DimAxis.Horizontal, AngleX("BA_VBL", LSide - vblSpan), topY, centerText: false);

        PlaceDim(ctx, diag, outList, "VBL", Side, DimAxis.Horizontal,
            S[0] + ssv * TD / 2.0 + 4.0,
            bottomY + vblSpan / 2.0,
            centerText: false);
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
        var TDF = LayoutMath.Dmm(ctx, "TDF");
        var T = LayoutMath.Dmm(ctx, "T");
        var FD = LayoutMath.Dmm(ctx, "FD");
        var G = LayoutMath.Dmm(ctx, "G");
        var ERD = LayoutMath.Dmm(ctx, "ERD");
        var CGD = LayoutMath.Dmm(ctx, "CGD");
        var FR = LayoutMath.Dmm(ctx, "FR");
        var F = LayoutMath.Dmm(ctx, "F");
        var ERL = LayoutMath.Dmm(ctx, "ERL");
        var C = LayoutMath.Dmm(ctx, "C");
        var BF = LayoutMath.Dmm(ctx, "BF");
        // The guarded full-thickness model has a hole exit at the section's upper-right corner.
        var fgProductionCustomer = (ctx.Drawing.DrawingType is DrawingType.Production or DrawingType.Customer) &&
            ctx.Wedge.Subclass == WedgeSubclass.FG;
        var noChamfer = fgProductionCustomer && TDF > 0 && T >= TDF;

        var HA = LayoutMath.Ddeg(ctx, "HA");
        var RA = LayoutMath.Ddeg(ctx, "RA");
        var RA2 = LayoutMath.Ddeg(ctx, "RA2");

        // Bottom stack (closest to the part first): G/F, BF, FL, T - 3 mm apart.
        PlaceDim(ctx, diag, outList, "T", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + T / 2.0 * scv,
            bandMidY - 14.0);

        PlaceDim(ctx, diag, outList, "FL", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + FL / 2.0 * scv,
            bandMidY - 11.0);

        PlaceDim(ctx, diag, outList, "BF", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + FR * scv + BF / 2 * scv, bandMidY - 8);

        PlaceDim(ctx, diag, outList, "F", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + FR * scv + F / 2.0 * scv,
            bandMidY - 5.0);

        PlaceDim(ctx, diag, outList, "G", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + G / 2.0 * scv,
            bandMidY - 5.0);

        PlaceDim(ctx, diag, outList, "FR", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv - 10.0,
            bandMidY + (fgProductionCustomer ? -2.0 : -3.0), centerText: !fgProductionCustomer);

        PlaceDim(ctx, diag, outList, "BR", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + FL * scv + (fgProductionCustomer ? 14.0 : 10.0),
            bandMidY + (fgProductionCustomer ? -2.0 : -3.0), centerText: !fgProductionCustomer);
        /*They said to remove it
        PlaceDim(ctx, diag, outList, "CGR", Section, DimAxis.Horizontal,
           Sec[0] - TDF / 2.0 * scv - 10.0,
           bandMidY - 6.0);
        */

        /*
        PlaceDim(ctx, diag, outList, "Y", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + FL * scv + 10.0,
            bandMidY - 6.0);
        */

        // FG production/customer: place Y beside FR, 3 mm lower, with text centering disabled.
        PlaceDim(ctx, diag, outList, "Y", Section, DimAxis.Horizontal,
            fgProductionCustomer ? Sec[0] - TDF / 2.0 * scv - 18.0 : Sec[0] - TDF / 2.0 * scv + T * scv + 2.0,
            fgProductionCustomer ? bandMidY - 5.0 : bandMidY + C * scv + 6.0,
            centerText: !fgProductionCustomer);

        /*They said to remove it
        PlaceDim(ctx, diag, outList, "CGD", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv - 10.0,
            bandMidY + CGD / 2.0 * scv);
        */

        PlaceDim(ctx, diag, outList, "C", Section, DimAxis.Horizontal,
            Sec[0] - TDF / 2.0 * scv + T * scv + (fgProductionCustomer ? 8.0 : 10.0),
            bandMidY + C * scv / 2.0, centerText: !fgProductionCustomer);


        /*
        PlaceDim(ctx, diag, outList, "H", Section, DimAxis.Horizontal,
           Sec[0] - (TDF / 2) * scv + T * scv, bandMidY + ((T - FD) * scv) * (Math.Tan(HA * (Math.PI / 180.0))));
        */

        // On the middle of the hole axis: the axis starts at the BR corner (x = FL) and runs at
        // HA; the visible hole is centred about C + H/2 above the bottom face.
        var H = LayoutMath.Dmm(ctx, "H");
        var holeMidRise = C + H / 2.0;
        var holeMidRun = holeMidRise / Math.Tan((HA > 0 ? HA : 45.0) * (Math.PI / 180.0));
        // FG production/customer: FNA text 7 mm above the hole exit on the T face (the hole axis
        // starts at the BR corner, x = FL, and rises at HA), which keeps it inside its arc. A fixed
        // height above C fell outside the arc on thick sections, and SolidWorks then moved the
        // text out with a leader past the border. Same height as before on the usual parts.
        var holeExitRise = Math.Max(0.0, T - FL) * scv * Math.Tan((HA > 0 ? HA : 45.0) * (Math.PI / 180.0));
        var fnaY = fgProductionCustomer
            ? bandMidY + Math.Max(C * scv + 5.0, holeExitRise + 7.0)
            : bandMidY + C * scv + 27.0;

        // FG production/customer: H text in line with the FR text, at the FNA height. SolidWorks
        // puts the diameter text about 17.5 mm left of its position point (the end of its leader
        // shelf), while the FR text sits on its point, so H's point is offset to line the texts up.
        PlaceDim(ctx, diag, outList, "H", Section, DimAxis.Horizontal,
           fgProductionCustomer ? Sec[0] - TDF / 2.0 * scv - 10.0 + 17.5 : Sec[0] - (TDF / 2) * scv + (FL + holeMidRun) * scv,
           fgProductionCustomer ? fnaY : bandMidY + holeMidRise * scv,
           centerText: !fgProductionCustomer && !noChamfer);

        // FG production/customer: NR in the FR/H column, halfway between FR and H.
        PlaceDim(ctx, diag, outList, "NR", Section, DimAxis.Horizontal,
            fgProductionCustomer ? Sec[0] - TDF / 2.0 * scv - 10.0 : Sec[0] - TDF / 2.0 * scv + FL * scv + 15.0,
            fgProductionCustomer ? (bandMidY - 2.0 + fnaY) / 2.0 : bandMidY + 5.0, centerText: !fgProductionCustomer);

        // Anchored to the T/C corner rather than (T - FD)·tan(HA): FD is optional for 4516, and
        // without it the text (and therefore the arc radius) ended up so high that the arc ran
        // off the right edge of the sheet.
        PlaceDim(ctx, diag, outList, "FNA", Section, DimAxis.Horizontal,
            Sec[0] - (TDF / 2) * scv + T * scv + (fgProductionCustomer ? 18.0 : 20.0),
            fnaY);

        // HA (D5). SolidWorks sets the arc radius from the text position, and text outside the
        // angle gets a leader. FG production/customer: keep the text on the angle's bisector
        // (vertex at the BR corner on the bottom line, where the hole axis starts) and move it
        // out until it is level with the upper part of the C step, next to the C dimension.
        var haDeg = HA > 0 ? HA : 45.0;
        var haRise = Math.Max(5.0, C * scv - 5.0);
        var haHalf = haDeg / 2.0 * (Math.PI / 180.0);
        PlaceDim(ctx, diag, outList, "D5", Section, DimAxis.Horizontal,
            fgProductionCustomer
                ? Sec[0] - TDF / 2.0 * scv + FL * scv + haRise / Math.Tan(haHalf)
                : Sec[0] - TDF / 2.0 * scv + T * scv + 20.0,
            bandMidY + (fgProductionCustomer ? haRise : 10.0), centerText: !fgProductionCustomer);

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
        double y,
        bool centerText = true)
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
                CenterText = centerText,
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
            CenterText = centerText,
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

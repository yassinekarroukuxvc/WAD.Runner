using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData
using WAD.Runner.DrawingAutomation.Interop;           // InteropCompat
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Computes a unified scale from the Front view outline so the views fill the sheet
/// without clipping, then applies that scale to Front/Side/Top.
/// - Uses only SW geometry (no wedge dimensions).
/// - Robust across interop versions via InteropCompat helpers.
/// </summary>
public sealed class ViewAutoScaleService
{
    public sealed record Policy(
        double FillRatioHeight,   // e.g., 0.80 → fill 80% of the sheet height
        double MinScale,          // e.g., 2.0
        double MaxScale,          // e.g., 8.0
        double Step,              // e.g., 0.5
        double TopMarginMm = 0.0, // reserved margins
        double BottomMarginMm = 0.0
    );

    private readonly DrawingService _ds;
    private readonly DrawingDoc _drawing;
    private readonly ModelDoc2 _model;

    public ViewAutoScaleService(DrawingService ds)
    {
        _ds = ds ?? throw new ArgumentNullException(nameof(ds));
        _drawing = ds.Drawing ?? throw new InvalidOperationException("No active drawing.");
        _model = ds.Model ?? throw new InvalidOperationException("No active drawing model.");
    }

    public double ApplyUnifiedScaleFromFront(
        DrawingData drawingData,
        Policy policy,
        IDictionary<string, string>? nameMap = null)
    {
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
        nameMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Front"] = "Front",
            ["Side"] = "Side",
            ["Top"] = "Top"
        };

        var frontName = nameMap.TryGetValue("Front", out var fn) ? fn : "Front";
        var front = ViewFinder.FindByName(_drawing, frontName);
        if (front is null)
        {
            Logger.Warn($"[AutoScale] Front view '{frontName}' not found. Using MinScale={policy.MinScale:0.###}.");
            WriteScaleBackToData(drawingData, policy.MinScale);
            return policy.MinScale;
        }

        // Start from at least MinScale
        InteropCompat.TrySetScale(front, Math.Max(InteropCompat.GetScaleDecimalOr(front, 1.0), policy.MinScale));
        HardRebuild();

        if (!TryGetSheetHeightMeters(out var sheetH_m))
        {
            Logger.Warn("[AutoScale] Could not read sheet size. Using MinScale.");
            WriteScaleBackToData(drawingData, policy.MinScale);
            return policy.MinScale;
        }

        var availableH_m = Math.Max(0.0,
            sheetH_m - (policy.TopMarginMm + policy.BottomMarginMm) / 1000.0);

        // Climb scale until outline height exceeds target fill, or we hit max
        var best = policy.MinScale;
        for (double s = policy.MinScale; s <= policy.MaxScale + 1e-9; s += policy.Step)
        {
            InteropCompat.TrySetScale(front, s);
            HardRebuild();

            if (!TryGetOutlineHeightMeters(front, out var hFront_m))
            {
                Logger.Warn("[AutoScale] Could not read front outline; stopping.");
                break;
            }

            var fills = hFront_m / availableH_m;
            if (fills <= policy.FillRatioHeight + 1e-9)
                best = s; // still fits → keep going
            else
                break;    // went too far → step back
        }

        // Apply the unified scale to all logical targets
        var sideName = nameMap.TryGetValue("Side", out var sn) ? sn : "Side";
        var topName = nameMap.TryGetValue("Top", out var tn) ? tn : "Top";

        var side = ViewFinder.FindByName(_drawing, sideName);
        var top = ViewFinder.FindByName(_drawing, topName);

        InteropCompat.TrySetScale(front, best);
        if (side is not null) InteropCompat.TrySetScale(side, best);
        if (top is not null) InteropCompat.TrySetScale(top, best);
        HardRebuild();

        WriteScaleBackToData(drawingData, best);

        Logger.Info($"[AutoScale] Unified scale = {best:0.###} (Fill={policy.FillRatioHeight:P0}, Range={policy.MinScale:0.###}..{policy.MaxScale:0.###}, Step={policy.Step:0.###}).");
        return best;
    }

    // ── internals ─────────────────────────────────────────────────────────

    private void HardRebuild()
    {
        try { _model.EditRebuild3(); } catch { }
        try { _model.GraphicsRedraw2(); } catch { }
    }

    private bool TryGetSheetHeightMeters(out double h)
    {
        h = 0.0;
        try
        {
            var sheet = _drawing.GetCurrentSheet() as Sheet;
            double w = 0, hh = 0;
            sheet?.GetSize(ref w, ref hh);
            h = hh;
            return h > 0;
        }
        catch { return false; }
    }

    private static bool TryGetOutlineHeightMeters(View v, out double h)
    {
        h = 0.0;
        if (!InteropCompat.TryGetViewOutline(v, out var _x1, out var y1, out var _x2, out var y2)) return false;
        h = Math.Abs(y2 - y1);
        return h > 0;
    }

    private static void WriteScaleBackToData(DrawingData drawingData, double unified)
    {
        SetScale(drawingData, "Front", unified);
        SetScale(drawingData, "Side", unified);
        SetScale(drawingData, "Top", unified);
    }

    private static void SetScale(DrawingData dd, string key, double value)
    {
        if (dd.Views.TryGetValue(key, out var cfg) && cfg is not null)
            cfg.Scale = value;
    }
}
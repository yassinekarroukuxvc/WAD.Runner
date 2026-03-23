using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;                    // Logger
using WAD.Runner.DataManagement.Domain.Drawing; // DrawingData
using WAD.Runner.DrawingAutomation.Interop;     // InteropCompat
using WAD.Runner.DrawingAutomation.SolidWorks;  // DrawingService

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Computes a unified scale from the Front view outline so the views fill the sheet
/// without clipping, then applies that scale to Front/Side/Top.
///
/// - Uses only SW geometry (no wedge dimensions).
/// - Robust across interop versions via InteropCompat helpers.
///
/// PERFORMANCE:
/// - Avoid redraw inside each search step.
/// - Reduce rebuild count by using binary search on the allowed stepped scale range.
/// - Cache sheet height once.
/// </summary>
public sealed class ViewAutoScaleService
{
    public sealed record Policy(
        double FillRatioHeight,
        double MinScale,
        double MaxScale,
        double Step,
        double TopMarginMm = 0.0,
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
        ValidatePolicy(policy);

        nameMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Front"] = "Front",
            ["Side"] = "Side",
            ["Top"] = "Top"
        };

        var frontName = nameMap.TryGetValue("Front", out var fn) ? fn : "Front";
        var sideName = nameMap.TryGetValue("Side", out var sn) ? sn : "Side";
        var topName = nameMap.TryGetValue("Top", out var tn) ? tn : "Top";

        var front = ViewFinder.FindByName(_drawing, frontName);
        if (front is null)
        {
            Logger.Warn($"[AutoScale] Front view '{frontName}' not found. Using MinScale={policy.MinScale:0.###}.");
            WriteScaleBackToData(drawingData, policy.MinScale);
            return policy.MinScale;
        }

        if (!TryGetSheetHeightMeters(out var sheetH_m))
        {
            Logger.Warn("[AutoScale] Could not read sheet size. Using MinScale.");
            WriteScaleBackToData(drawingData, policy.MinScale);
            return policy.MinScale;
        }

        var availableH_m = Math.Max(
            0.0,
            sheetH_m - (policy.TopMarginMm + policy.BottomMarginMm) / 1000.0);

        if (availableH_m <= 0.0)
        {
            Logger.Warn("[AutoScale] Available sheet height is 0 after margins. Using MinScale.");
            WriteScaleBackToData(drawingData, policy.MinScale);
            return policy.MinScale;
        }

        // Generate stepped scale candidates once.
        var candidates = BuildScaleCandidates(policy);
        if (candidates.Count == 0)
        {
            Logger.Warn("[AutoScale] No valid scale candidates. Using MinScale.");
            WriteScaleBackToData(drawingData, policy.MinScale);
            return policy.MinScale;
        }

        // Ensure front is at least on a known valid starting scale.
        InteropCompat.TrySetScale(front, candidates[0]);
        RebuildOnly();

        // Binary search for largest scale that still fits.
        int lo = 0;
        int hi = candidates.Count - 1;
        int bestIdx = 0;

        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            double s = candidates[mid];

            InteropCompat.TrySetScale(front, s);
            RebuildOnly();

            if (!TryGetOutlineHeightMeters(front, out var hFront_m))
            {
                Logger.Warn("[AutoScale] Could not read front outline; using current best.");
                break;
            }

            double fills = hFront_m / availableH_m;

            if (fills <= policy.FillRatioHeight + 1e-9)
            {
                bestIdx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        double best = candidates[bestIdx];

        var side = ViewFinder.FindByName(_drawing, sideName);
        var top = ViewFinder.FindByName(_drawing, topName);

        _ds.RunInFastMode(() =>
        {
            InteropCompat.TrySetScale(front, best);
            if (side is not null) InteropCompat.TrySetScale(side, best);
            if (top is not null) InteropCompat.TrySetScale(top, best);
        });

        RebuildOnly();
        WriteScaleBackToData(drawingData, best);

        Logger.Info(
            $"[AutoScale] Unified scale = {best:0.###} " +
            $"(Fill={policy.FillRatioHeight:P0}, Range={policy.MinScale:0.###}..{policy.MaxScale:0.###}, Step={policy.Step:0.###}).");

        return best;
    }

    // ── internals ─────────────────────────────────────────────────────────

    private void RebuildOnly()
    {
        try { _model.EditRebuild3(); } catch { }
    }

    private bool TryGetSheetHeightMeters(out double h)
    {
        h = 0.0;

        try
        {
            var sheet = _drawing.GetCurrentSheet() as Sheet;
            if (sheet == null)
                return false;

            double w = 0.0;
            double hh = 0.0;
            sheet.GetSize(ref w, ref hh);

            h = hh;
            return h > 0.0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetOutlineHeightMeters(View v, out double h)
    {
        h = 0.0;
        if (!InteropCompat.TryGetViewOutline(v, out _, out var y1, out _, out var y2))
            return false;

        h = Math.Abs(y2 - y1);
        return h > 0.0;
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

    private static void ValidatePolicy(Policy policy)
    {
        if (policy.Step <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(policy.Step), "Step must be > 0.");

        if (policy.MinScale <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(policy.MinScale), "MinScale must be > 0.");

        if (policy.MaxScale < policy.MinScale)
            throw new ArgumentOutOfRangeException(nameof(policy.MaxScale), "MaxScale must be >= MinScale.");

        if (policy.FillRatioHeight <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(policy.FillRatioHeight), "FillRatioHeight must be > 0.");
    }

    private static List<double> BuildScaleCandidates(Policy policy)
    {
        var result = new List<double>();

        double s = policy.MinScale;
        int guard = 0;

        while (s <= policy.MaxScale + 1e-9 && guard++ < 10000)
        {
            result.Add(RoundScale(s));
            s += policy.Step;
        }

        if (result.Count == 0 || result[^1] < policy.MaxScale - 1e-9)
            result.Add(RoundScale(policy.MaxScale));

        // Distinct in case rounding created duplicates.
        var deduped = new List<double>(result.Count);
        double? last = null;
        for (int i = 0; i < result.Count; i++)
        {
            var current = result[i];
            if (last is null || Math.Abs(current - last.Value) > 1e-9)
            {
                deduped.Add(current);
                last = current;
            }
        }

        return deduped;
    }

    private static double RoundScale(double value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
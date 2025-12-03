// PartAutomation/Rules/BasicPartRules.cs
using System;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.Common;     // ← add this
using WAD.Runner.PartAutomation.SolidWorks;

namespace WAD.Runner.PartAutomation.Rules;

public static class BasicPartRules
{
    public static void ApplyVrMinMax(PartEditor part, WedgeData wedge)
    {
        Logger.Info("[BasicPartRules] ApplyVrMinMax → start");
        if (!wedge.Dimensions.TryGetValue(new DimensionKey("VR"), out var vr))
        {
            Logger.Warn("[BasicPartRules] VR not found; skipping VR_MIN/VR_MAX.");
            return;
        }
        if (!vr.Nominal.IsMm)
        {
            Logger.Warn("[BasicPartRules] VR is not in mm; skipping VR_MIN/VR_MAX.");
            return;
        }

        var vr_m = (double)vr.Nominal.AsMm() / 1000.0;
        var lo_m = (double)vr.Tol.Lower.AsMm() / 1000.0;
        var up_m = (double)vr.Tol.Upper.AsMm() / 1000.0;

        Logger.Info($"[BasicPartRules] VR_m={vr_m:F6}  LO_m={lo_m:F6}  UP_m={up_m:F6}");
        part.SetDimensionMeters(SwNames.DimVrMin, vr_m - lo_m);
        part.SetDimensionMeters(SwNames.DimVrMax, vr_m + up_m);
        Logger.Success("[BasicPartRules] VR_MIN/VR_MAX applied.");
    }

    public static void ApplyVwTolDims(PartEditor part, WedgeData wedge)
    {
        Logger.Info("[BasicPartRules] ApplyVwTolDims → start");
        if (!wedge.Dimensions.TryGetValue(new DimensionKey("VW"), out var vw))
        {
            Logger.Warn("[BasicPartRules] VW not found; skipping VW tolerances.");
            return;
        }

        var lt_m = (double)vw.Tol.Lower.AsMm() / 1000.0;
        var ut_m = (double)vw.Tol.Upper.AsMm() / 1000.0;

        Logger.Info($"[BasicPartRules] VW tolerances (m): LTOL={lt_m:F6}, UTOL={ut_m:F6}");
        part.SetDimensionMeters(SwNames.DimVwLTol, lt_m);
        part.SetDimensionMeters(SwNames.DimVwUTol, ut_m);
        Logger.Success("[BasicPartRules] VW_LTOL/VW_UTOL applied.");
    }

    public static void ApplyOverlayVwWToggle(PartEditor part, WedgeData wedge, bool overlay)
    {
        Logger.Info($"[BasicPartRules] ApplyOverlayVwWToggle → overlay={overlay}");
        if (!overlay)
        {
            Logger.Blue("[BasicPartRules] Not an Overlay drawing; skip VW/W toggle.");
            return;
        }

        bool hasVW = wedge.Dimensions.TryGetValue(new DimensionKey("VW"), out var vw) && vw.Nominal.IsMm;
        bool hasW = wedge.Dimensions.TryGetValue(new DimensionKey("W"), out var w) && w.Nominal.IsMm;

        if (!(hasVW && hasW))
        {
            Logger.Warn("[BasicPartRules] Missing VW or W (or not mm); defaulting to W sketch.");
            part.SuppressSketch(SwNames.SketchFgWedW, suppress: false);
            part.SuppressSketch(SwNames.SketchFgWedVW, suppress: true);
            return;
        }

        var vwMm = vw.Nominal.AsMm();
        var wMm = w.Nominal.AsMm();
        var equal = Math.Abs((double)(vwMm - wMm)) <= 0.000001;

        Logger.Info($"[BasicPartRules] VW={vwMm} mm, W={wMm} mm, equal≈{equal}");
        if (equal)
        {
            Logger.Info("[BasicPartRules] VW≈W → enable FG_Wed_VW, disable FG_Wed_W");
            part.SuppressSketch(SwNames.SketchFgWedW, suppress: true);
            part.SuppressSketch(SwNames.SketchFgWedVW, suppress: false);
        }
        else
        {
            Logger.Info("[BasicPartRules] VW≠W → enable FG_Wed_W, disable FG_Wed_VW");
            part.SuppressSketch(SwNames.SketchFgWedW, suppress: false);
            part.SuppressSketch(SwNames.SketchFgWedVW, suppress: true);
        }
        Logger.Success("[BasicPartRules] VW/W toggle applied.");
    }

    public static void ApplyTipGuard(PartEditor part, WedgeData wedge)
    {
        Logger.Info("[BasicPartRules] ApplyTipGuard → start");
        if (!wedge.Dimensions.TryGetValue(new DimensionKey("TIP"), out var tip))
        {
            Logger.Blue("[BasicPartRules] TIP not present; nothing to guard.");
            return;
        }
        if (!tip.Nominal.IsMm)
        {
            Logger.Warn("[BasicPartRules] TIP not in mm; skipping TIP guard.");
            return;
        }

        var zero = tip.Nominal.AsMm() == 0m;
        Logger.Info($"[BasicPartRules] TIP={tip.Nominal.AsMm()} mm → suppress {SwNames.SketchCrmet} = {zero}");
        part.SuppressSketch(SwNames.SketchCrmet, suppress: zero);
        Logger.Success("[BasicPartRules] TIP guard applied.");
    }
    public static void ApplyEngravingToggle(PartEditor part)
    {
        Logger.Info("[BasicPartRules] ApplyEngravingToggle → enable engraving for non-overlay drawings");

        // For Production / Customer: engraving ON (unsuppressed)
        const bool suppress = false;

        Logger.Info($"[BasicPartRules] Engraving sketch '{SwNames.Engraving}' suppress={suppress}");
        part.SuppressSketch(SwNames.Engraving, suppress: suppress);

        Logger.Success("[BasicPartRules] Engraving toggle applied (non-overlay only).");
    }

}

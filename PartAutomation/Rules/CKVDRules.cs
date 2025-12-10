// PartAutomation/Rules/CKVDRules.cs
using System;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.PartAutomation.SolidWorks;
using WAD.Runner.PartAutomation.Common;

namespace WAD.Runner.PartAutomation.Rules;

public static class CKVDRules
{
    /// <summary>
    /// CKVD post-rules orchestration.
    /// </summary>
    public static void Apply(PartEditor part, WedgeData wedge, DrawingType drawingType)
    {
        Logger.Info("[CKVDRules] Apply → start");

        // Currently only FG has defined CKVD rules.
        if (wedge.Subclass != WedgeSubclass.FG)
        {
            Logger.Info("[CKVDRules] Subclass is not FG; applying engraving (if non-overlay) and exiting.");

            if (drawingType == DrawingType.Production || drawingType == DrawingType.Customer)
            {
                BasicPartRules.ApplyEngravingToggle(part);
            }

            part.Rebuild();
            Logger.Success("[CKVDRules] Apply → done (non-FG).");
            return;
        }

        // Non-overlay CKVD FG → engraving ON
        if (drawingType == DrawingType.Production || drawingType == DrawingType.Customer)
        {
            BasicPartRules.ApplyEngravingToggle(part);
        }

        ApplyTipGuard(part, wedge);
        ApplyVrMinMax(part, wedge);
        ApplyVwTolDims(part, wedge);
        ApplyOverlayVwWToggle(part, wedge, overlay: drawingType == DrawingType.Overlay);

        part.Rebuild();
        Logger.Success("[CKVDRules] Apply → done.");
    }

    private static void ApplyVrMinMax(PartEditor part, WedgeData wedge)
    {
        Logger.Info("[CKVDRules] ApplyVrMinMax → start");

        if (!wedge.Dimensions.TryGetValue(new DimensionKey("VR"), out var vr))
        {
            Logger.Warn("[CKVDRules] VR not found; skipping VR_MIN/VR_MAX.");
            return;
        }
        if (!vr.Nominal.IsMm)
        {
            Logger.Warn("[CKVDRules] VR is not in mm; skipping VR_MIN/VR_MAX.");
            return;
        }

        var vr_m = (double)vr.Nominal.AsMm() / 1000.0;
        var lo_m = (double)vr.Tol.Lower.AsMm() / 1000.0;
        var up_m = (double)vr.Tol.Upper.AsMm() / 1000.0;

        Logger.Info($"[CKVDRules] VR_m={vr_m:F6}  LO_m={lo_m:F6}  UP_m={up_m:F6}");
        part.SetDimensionMeters(SwNames.DimVrMin, vr_m - lo_m);
        part.SetDimensionMeters(SwNames.DimVrMax, vr_m + up_m);
        Logger.Success("[CKVDRules] VR_MIN/VR_MAX applied.");
    }

    private static void ApplyVwTolDims(PartEditor part, WedgeData wedge)
    {
        Logger.Info("[CKVDRules] ApplyVwTolDims → start");

        if (!wedge.Dimensions.TryGetValue(new DimensionKey("VW"), out var vw))
        {
            Logger.Warn("[CKVDRules] VW not found; skipping VW tolerances.");
            return;
        }

        var lt_m = (double)vw.Tol.Lower.AsMm() / 1000.0;
        var ut_m = (double)vw.Tol.Upper.AsMm() / 1000.0;

        Logger.Info($"[CKVDRules] VW tolerances (m): LTOL={lt_m:F6}, UTOL={ut_m:F6}");
        part.SetDimensionMeters(SwNames.DimVwLTol, lt_m);
        part.SetDimensionMeters(SwNames.DimVwUTol, ut_m);
        Logger.Success("[CKVDRules] VW_LTOL/VW_UTOL applied.");
    }

    private static void ApplyOverlayVwWToggle(PartEditor part, WedgeData wedge, bool overlay)
    {
        Logger.Info($"[CKVDRules] ApplyOverlayVwWToggle → overlay={overlay}");
        if (!overlay)
        {
            Logger.Blue("[CKVDRules] Not an Overlay drawing; skip VW/W toggle.");
            return;
        }

        bool hasVW = wedge.Dimensions.TryGetValue(new DimensionKey("VW"), out var vw) && vw.Nominal.IsMm;
        bool hasW = wedge.Dimensions.TryGetValue(new DimensionKey("W"), out var w) && w.Nominal.IsMm;

        if (!(hasVW && hasW))
        {
            Logger.Warn("[CKVDRules] Missing VW or W (or not mm); defaulting to W sketch.");
            part.SuppressSketch(SwNames.SketchFgWedW, suppress: false);
            part.SuppressSketch(SwNames.SketchFgWedVW, suppress: true);
            return;
        }

        var vwMm = vw.Nominal.AsMm();
        var wMm = w.Nominal.AsMm();
        var equal = Math.Abs((double)(vwMm - wMm)) <= 0.000001;

        Logger.Info($"[CKVDRules] VW={vwMm} mm, W={wMm} mm, equal≈{equal}");
        if (equal)
        {
            Logger.Info("[CKVDRules] VW≈W → enable FG_Wed_VW, disable FG_Wed_W");
            part.SuppressSketch(SwNames.SketchFgWedW, suppress: true);
            part.SuppressSketch(SwNames.SketchFgWedVW, suppress: false);
        }
        else
        {
            Logger.Info("[CKVDRules] VW≠W → enable FG_Wed_W, disable FG_Wed_VW");
            part.SuppressSketch(SwNames.SketchFgWedW, suppress: false);
            part.SuppressSketch(SwNames.SketchFgWedVW, suppress: true);
        }
        Logger.Success("[CKVDRules] VW/W toggle applied.");
    }

    private static void ApplyTipGuard(PartEditor part, WedgeData wedge)
    {
        Logger.Info("[CKVDRules] ApplyTipGuard → start");

        if (!wedge.Dimensions.TryGetValue(new DimensionKey("TIP"), out var tip))
        {
            Logger.Blue("[CKVDRules] TIP not present; nothing to guard.");
            return;
        }
        if (!tip.Nominal.IsMm)
        {
            Logger.Warn("[CKVDRules] TIP not in mm; skipping TIP guard.");
            return;
        }

        var zero = tip.Nominal.AsMm() == 0m;
        Logger.Info($"[CKVDRules] TIP={tip.Nominal.AsMm()} mm → suppress {SwNames.SketchCrmet} = {zero}");
        part.SuppressSketch(SwNames.SketchCrmet, suppress: zero);
        Logger.Success("[CKVDRules] TIP guard applied.");
    }
}

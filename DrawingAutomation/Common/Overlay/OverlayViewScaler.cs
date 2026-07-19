using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Common.Overlay
{
    public static class OverlayViewScaler
    {
        public static void ApplyOverlayViewScales(DrawingService ds, IDictionary<string, string> nameMap, double overlayMag)
        {
            try
            {
                var drawing = ds.Drawing;
                if (drawing == null)
                {
                    Logger.Warn("[Overlay] ApplyOverlayViewScales: drawing is null.");
                    return;
                }

                nameMap.TryGetValue("Front", out var frontName);
                nameMap.TryGetValue("Side", out var sideName);
                nameMap.TryGetValue("Top", out var topName);
                nameMap.TryGetValue("Detail", out var detailName);
                nameMap.TryGetValue("Section", out var sectionName);

                bool IsSame(string? a, string? b) =>
                    !string.IsNullOrWhiteSpace(a) &&
                    !string.IsNullOrWhiteSpace(b) &&
                    string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

                double detailSectionScale = OverlayMagnificationService.GetViewScale(overlayMag);

                object viewObj = drawing.GetFirstView();
                while (viewObj is not null)
                {
                    var view = viewObj as View;
                    if (view == null) break;

                    var vName = view.Name ?? string.Empty;

                    if (IsSame(vName, frontName) || IsSame(vName, sideName) || IsSame(vName, topName))
                    {
                        view.ScaleDecimal = 2;
                        Logger.Info($"[Overlay] Set view '{vName}' scale ≈ 1:1.5 (ScaleDecimal=2).");
                    }

                    if (IsSame(vName, detailName) || IsSame(vName, sectionName))
                    {
                        view.ScaleDecimal = detailSectionScale;
                        Logger.Info($"[Overlay] Set view '{vName}' scale from overlayMag={overlayMag}X → ScaleDecimal={detailSectionScale:0.####}.");
                    }

                    viewObj = view.GetNextView();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] ApplyOverlayViewScales failed: {ex.Message}");
            }
        }

        // Compatibility overload retained for existing callers outside DrawingAutomation.
        public static void TryRepositionAllOverlayViews(
            SldWorks swApp,
            DrawingService ds,
            DrawingRun run,
            IDictionary<string, string> nameMap)
        {
            if (run is null) throw new ArgumentNullException(nameof(run));

            var overlayMagnification = OverlayMagnificationService.ComputeMagnification(
                run.Wedge,
                run.WedgeType);

            TryRepositionAllOverlayViews(
                swApp,
                ds,
                run,
                nameMap,
                overlayMagnification);
        }

        public static void TryRepositionAllOverlayViews(
            SldWorks swApp,
            DrawingService ds,
            DrawingRun run,
            IDictionary<string, string> nameMap,
            double overlayMagnification)
        {
            try
            {
                var overlayMacroPath = GetOverlayMacroPath();

                var behavior = DrawingWedgeBehaviorCatalog.Get(run.WedgeType);
                var facts = new DrawingWedgeFacts(run.Wedge);
                var isCobLike = behavior.Family == DrawingWedgeFamily.CobLike;
                var isOSG7 = behavior.Family == DrawingWedgeFamily.Osg7;
                var hasVw = facts.HasPositiveLength("VW");
                var hasVr = facts.HasPositiveLength("VR");

                var baseRefPointSketchName = facts.ShankType == DrawingShankType.Reverse180
                    ? "ref_point_180_DEG_REV_sketch"
                    : behavior.OverlayReferencePointSketch;

                var detailRefPointSketchName = baseRefPointSketchName;
                var sectionRefPointSketchName = baseRefPointSketchName;

                if (isCobLike && hasVw && hasVr)
                {
                    detailRefPointSketchName = "ref_point_non_std_cut_sketch";

                    Logger.Info(
                        "[Overlay] Non-CKVD with VW>0 and VR>0 detected → " +
                        "Detail view will use 'ref_point_non_std_cut_sketch'.");
                }

                double detailYIn = 2.4;
                double sectionYIn = 2.4;

                if (isCobLike)
                {
                    double tdfMm = facts.GetLengthMmOrNaN("TDF");
                    double tdMm = facts.GetLengthMmOrNaN("TD");

                    if (IsPositiveFinite(tdfMm) && IsPositiveFinite(tdMm))
                    {
                        double overlayScale = OverlayMagnificationService.GetViewScale(overlayMagnification);
                        double scaledTdfMm = tdfMm * overlayScale;
                        double scaledTdMm = tdMm * overlayScale;
                        double computedYmm = 60.96 - ((scaledTdfMm - (scaledTdMm / 2.0)) / 2.0);

                        Logger.Blue($"[Overlay] Raw TDF = {tdfMm:0.####} mm");
                        Logger.Blue($"[Overlay] Raw TD  = {tdMm:0.####} mm");
                        Logger.Blue($"[Overlay] OverlayMag = {overlayMagnification:0.####}X");
                        Logger.Blue($"[Overlay] OverlayScale = {overlayScale:0.####}");
                        Logger.Blue($"[Overlay] Scaled TDF = {scaledTdfMm:0.####} mm");
                        Logger.Blue($"[Overlay] Scaled TD  = {scaledTdMm:0.####} mm");
                        Logger.Blue($"[Overlay] Computed Detail/Section Y = {computedYmm:0.####} mm");

                        if (IsPositiveFinite(computedYmm))
                        {
                            double computedYin = MmToIn(computedYmm);
                            bool reverse180 = facts.ShankType == DrawingShankType.Reverse180;

                            detailYIn = computedYin;
                            sectionYIn = reverse180 ? 2.4 : computedYin;

                            Logger.Info(
                                $"[Overlay] {(reverse180 ? "Reverse-180" : "Standard")} shank → " +
                                $"Detail Y={detailYIn:0.####} in, Section Y={sectionYIn:0.####} in.");
                        }
                        else
                        {
                            Logger.Warn(
                                $"[Overlay] Computed Y was invalid ({computedYmm:0.####} mm). " +
                                "Falling back to 2.4 in.");
                        }
                    }
                    else
                    {
                        Logger.Warn(
                            $"[Overlay] Missing/invalid dimensions for Y calculation. " +
                            $"TDF={tdfMm:0.####} mm, TD={tdMm:0.####} mm. Falling back to 2.4 in.");
                    }
                }

                else if(isOSG7)
                {
                    double tdfMm = facts.GetLengthMmOrNaN("TDF");
                    double tdMm = facts.GetLengthMmOrNaN("TD");
                    double fxMm = facts.GetLengthMmOrNaN("FX");
                    double xMm = facts.GetLengthMmOrNaN("X");
                    double flMm = facts.GetLengthMmOrNaN("FL");
                    if (xMm == 0 || double.IsNaN(xMm))
                        xMm = tdfMm - (fxMm + flMm);

                    if (fxMm == 0 || double.IsNaN(fxMm))
                        fxMm = tdfMm - (xMm + flMm);


                    double overlayScale = OverlayMagnificationService.GetViewScale(overlayMagnification);
                    double scaledTdfMm = tdfMm * overlayScale;
                    double scaledTdMm = tdMm * overlayScale;
                    double scaledFxMm = fxMm * overlayScale;
                    double scaledFlMm = flMm * overlayScale;
                    
                    double centerofthepart = scaledTdMm / 2;
                    double centeroftheview = scaledTdfMm - scaledFxMm - scaledFlMm/2 ;
                    double distance = centerofthepart - centeroftheview;
                    //double computedY = 60.96 - ((scaledTdfMm - (scaledTdMm / 2.0)) / 2.0);
                    //double computedY = 60.96 + (tdfMm * overlayScale / 2) + (fxMm * overlayScale);
                    double computedY = 60.96 + distance;
                    computedY = MmToIn(computedY);
                    detailYIn = computedY;
                    sectionYIn = computedY;
                }


                Logger.Info(
                    $"[Overlay] Reposition Detail using sketch '{detailRefPointSketchName}', " +
                    $"Section using sketch '{sectionRefPointSketchName}', wedge type '{run.WedgeType}'.");

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Detail",
                    sketchName: detailRefPointSketchName,
                    xIn: 6.285,
                    yIn: 2.4, // keep this static this is the correct value
                    logicalToActual: nameMap);

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Section",
                    sketchName: sectionRefPointSketchName,
                    xIn: 3.19,
                    yIn: sectionYIn,
                    logicalToActual: nameMap);

                if (behavior.RepositionPrimaryOverlayViews)
                {
                    SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                        swApp,
                        macroFile: overlayMacroPath,
                        logicalViewName: "Front",
                        sketchName: baseRefPointSketchName,
                        xIn: 0.4,
                        yIn: 0.3,
                        logicalToActual: nameMap);

                    SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                        swApp,
                        macroFile: overlayMacroPath,
                        logicalViewName: "Side",
                        sketchName: baseRefPointSketchName,
                        xIn: 3.19,
                        yIn: 0,
                        logicalToActual: nameMap);

                    Logger.Info($"[Overlay] {run.WedgeType} profile requires Front and Side repositioning.");
                }
                else
                {
                    Logger.Info($"[Overlay] {run.WedgeType} profile repositions only Detail and Section views.");
                }

                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Reposition-after-scaling step failed (continuing): {ex.Message}");
            }
        }

        public static void DeleteFrontViewIfVrZero(DrawingService ds, IDictionary<string, string> nameMap, LayoutContext ctx)
        {
            try
            {
                double vr = LayoutMath.Dmm(ctx, "VR");

                if (double.IsNaN(vr) || double.IsInfinity(vr))
                {
                    Logger.Info("[Overlay] VR missing/invalid; keeping Front view.");
                    return;
                }

                if (Math.Abs(vr) > 1e-6)
                {
                    Logger.Info($"[Overlay] VR={vr:0.####} mm (non-zero); keeping Front view.");
                    return;
                }

                var model = ds.Model;
                if (model is null)
                {
                    Logger.Warn("[Overlay] DeleteFrontViewIfVrZero: model is null.");
                    return;
                }

                if (!nameMap.TryGetValue("Front", out var frontName) || string.IsNullOrWhiteSpace(frontName))
                    frontName = "Front";

                Logger.Info($"[Overlay] VR=0 → deleting Front view '{frontName}'…");

                model.ClearSelection2(true);

                bool sel = model.Extension.SelectByID2(
                    frontName,
                    "DRAWINGVIEW",
                    0, 0, 0,
                    false,
                    0,
                    null,
                    0);

                if (!sel)
                {
                    Logger.Warn($"[Overlay] Could not select Front view '{frontName}' for deletion.");
                    return;
                }

                bool ok = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);

                if (ok) Logger.Info($"[Overlay] Front view '{frontName}' deleted because VR=0.");
                else Logger.Warn($"[Overlay] VR=0 but deletion of Front view '{frontName}' failed.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] DeleteFrontViewIfVrZero failed: {ex.Message}");
            }
        }

        private static string GetOverlayMacroPath()
        {
            var baseDir = AppContext.BaseDirectory ?? string.Empty;

            var candidateOutput = Path.Combine(baseDir, "Resources", "Macros", "OverlayMacro.swp");
            candidateOutput = Path.GetFullPath(candidateOutput);
            if (File.Exists(candidateOutput))
            {
                Logger.Info($"[Overlay] Using macro from output folder: {candidateOutput}");
                return candidateOutput;
            }

            var candidateProject = Path.Combine(baseDir, "..", "..", "..", "Resources", "Macros", "OverlayMacro.swp");
            candidateProject = Path.GetFullPath(candidateProject);
            if (File.Exists(candidateProject))
            {
                Logger.Info($"[Overlay] Using macro from project folder: {candidateProject}");
                return candidateProject;
            }

            Logger.Warn("[Overlay] OverlayMacro.swp not found. " +
                        $"Tried: '{candidateOutput}' and '{candidateProject}'.");

            return candidateOutput;
        }

        private static bool IsPositiveFinite(double value)
            => double.IsFinite(value) && value > 0.0;

        private static double MmToIn(double mm) => mm / 25.4;

    }
}

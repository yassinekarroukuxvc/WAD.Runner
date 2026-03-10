// DrawingAutomation/Executors/PGB/PgbOverlayDrawingExecutor.cs
using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Executors.Common;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Views;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Executors.PGB
{
    public static class PgbOverlayDrawingExecutor
    {
        public static void Run(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            Func<object?> runPartAutomation)
            => Run(swApp, run, drawingData, runPartAutomation, plannedDims: null);

        public static void Run(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            Func<object?> runPartAutomation,
            IEnumerable<AnnotationPositioner.Plan>? plannedDims)
        {
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
            if (runPartAutomation is null) throw new ArgumentNullException(nameof(runPartAutomation));

            const string bannerLabel = "PGB/Overlay";
            Logger.Info($"=== WAD ▶ {bannerLabel} (executor-owned pipeline) ===");

            var profile = ProfileRegistry.Get(run.Wedge.Subclass, drawingData.DrawingType);
            Logger.Info($"[Profile] Using {profile.ProfileName} for {bannerLabel}.");

            // 1) Part automation
            Logger.Info("[1/10] Part Automation…");
            _ = runPartAutomation();

            // 2) Open + relink + sheet prep + nameMap
            Logger.Info("[2/10] Open + relink + sheet prep…");
            var ds = OverlayDrawingExecutorCommon.OpenRelinkAndPrepareOverlaySheet(
                swApp,
                run,
                drawingData,
                out var nameMap);

            // 2b) ✅ PGB-specific step
            Logger.Info("[2b/10] PGB overlay: bind view referenced configurations…");
            //TryBindReferencedConfigsForPgbOverlay(ds, nameMap, run, drawingData);

            // 3) Compute overlay mag/cal + payload
            Logger.Info("[3/10] Compute overlay magnification/calibration…");
            var (ctx, overlayMag, overlayCalUm) = OverlayDrawingExecutorCommon.ComputeOverlayMagCal(run, drawingData);

            Logger.Info("[3b/10] Build overlay payload for dimension table…");
            var overlayKeys = OverlayDrawingExecutorCommon.DefaultOverlayDimKeys(run.WedgeType);
            var overlayData = OverlayDrawingExecutorCommon.BuildOverlayPayload(run, drawingData, overlayKeys);

            ds.Rebuild();

            // 4) Apply overlay scales
            Logger.Info("[4/10] Apply overlay view scales…");
            OverlayDrawingExecutorCommon.ApplyOverlayViewScales(ds, nameMap, overlayMag);

            // 5) Reposition views via macro
            Logger.Info("[5/10] Reposition all overlay views (macro)…");
            //OverlayDrawingExecutorCommon.TryRepositionAllOverlayViews(swApp, ds, run, nameMap);

            // 6) Delete Front view when VR == 0
            Logger.Info("[6/10] Delete Front view if VR=0…");
            //OverlayDrawingExecutorCommon.DeleteFrontViewIfVrZero(ds, nameMap, ctx);

            ds.Rebuild();
            ds.ZoomToSheet();

            // 7) Plan dims
            Logger.Info("[7/10] Plan overlay dimensions…");
            var (dims, planned) = OverlayDrawingExecutorCommon.PlanOverlayDimensions(ctx, run.WedgeType, plannedDims);

            // 8) Table
            Logger.Info("[8/10] Create overlay dimension table…");
            OverlayDrawingExecutorCommon.TryCreateOverlayDimTable(swApp, ds, drawingData, overlayData);

            // 9) Apply positions + metadata + cleanup + final
            Logger.Info("[9/10] Apply annotation positions…");
            OverlayDrawingExecutorCommon.TryApplyAnnotationPositions(ds, nameMap, run, drawingData, planned);

            Logger.Info("[9b/10] Apply overlay metadata…");
            OverlayDrawingExecutorCommon.TryApplyOverlayMetadata(ds, drawingData, run);

            Logger.Info("[9c/10] Cleanup zero-valued overlay dimensions…");
            OverlayDrawingExecutorCommon.TryCleanupZeroDims(ds, nameMap, ctx, drawingData, dims);

            Logger.Info("[Final-Prep] Draw calibration box + note…");
            OverlayDrawingExecutorCommon.TryCalibrationBoxAndNote(ds, overlayMag, overlayCalUm);

            Logger.Info("[Final] Export overlay drawing (TIFF)…");
            OverlayDrawingExecutorCommon.ExportOverlayTiff(swApp, ds, run);

            Logger.Success($"{bannerLabel} drawing execution completed.");
        }

        private static void TryBindReferencedConfigsForPgbOverlay(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            DrawingRun run,
            DrawingData drawingData)
        {
            try
            {
                var model = ds.Model as ModelDoc2;
                if (model == null) return;

                nameMap.TryGetValue("Front", out var frontViewName);
                nameMap.TryGetValue("Side", out var sideViewName);
                nameMap.TryGetValue("Top", out var topViewName);
                nameMap.TryGetValue("Detail", out var detailViewName);
                nameMap.TryGetValue("Section", out var sectionViewName);

                var subclass = run.Wedge.Subclass;
                var overlayType = drawingData.DrawingType;
                var prodType = DrawingType.Production;

                if (!string.IsNullOrWhiteSpace(frontViewName))
                    DrawingViewConfigBinder.SetReferencedConfigurationForView(model, frontViewName, subclass, prodType);

                if (!string.IsNullOrWhiteSpace(sideViewName))
                    DrawingViewConfigBinder.SetReferencedConfigurationForView(model, sideViewName, subclass, prodType);

                if (!string.IsNullOrWhiteSpace(topViewName))
                    DrawingViewConfigBinder.SetReferencedConfigurationForView(model, topViewName, subclass, prodType);

                if (!string.IsNullOrWhiteSpace(detailViewName))
                    DrawingViewConfigBinder.SetReferencedConfigurationForView(model, detailViewName, subclass, overlayType);

                if (!string.IsNullOrWhiteSpace(sectionViewName))
                    DrawingViewConfigBinder.SetReferencedConfigurationForView(model, sectionViewName, subclass, overlayType);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[PGB/Overlay] Config binding failed (continuing): {ex.Message}");
            }
        }
    }
}
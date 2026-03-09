// DrawingAutomation/Executors/FG/FgOverlayDrawingExecutor.cs
using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Executors.Common;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Executors.FG
{
    public static class FgOverlayDrawingExecutor
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

            const string bannerLabel = "FG/Overlay";
            Logger.Info($"=== WAD ▶ {bannerLabel} (executor-owned pipeline) ===");

            var profile = run.WedgeType switch
            {
                WedgeType.COB => ProfileRegistry.GetCob(run.Wedge.Subclass, drawingData.DrawingType),
                WedgeType.OSG7 => ProfileRegistry.GetOsg7(run.Wedge.Subclass, drawingData.DrawingType),
                _ => ProfileRegistry.GetCkvd(run.Wedge.Subclass, drawingData.DrawingType)
            };
            Logger.Info($"[Profile] Using {profile.ProfileName} for {bannerLabel}.");

            // 1) Part automation
            Logger.Info("[1/9] Part Automation…");
            _ = runPartAutomation();

            // 2) Open + relink + overlay sheet prep + nameMap
            Logger.Info("[2/9] Open + relink + sheet prep…");
            var ds = OverlayDrawingExecutorCommon.OpenRelinkAndPrepareOverlaySheet(
                swApp,
                run,
                drawingData,
                out var nameMap);

            // 2b) FG-specific hook area (wedge-type specific stuff)
            // Example pattern:
            // switch (run.WedgeType) { case WedgeType.CKVD: ...; break; case WedgeType.COB: ...; break; }

            // 3) Compute overlay mag/cal + payload
            Logger.Info("[3/9] Compute overlay magnification/calibration from FL…");
            var (ctx, overlayMag, overlayCalUm) = OverlayDrawingExecutorCommon.ComputeOverlayMagCalFromFl(run, drawingData);

            Logger.Info("[3b/9] Build overlay payload for dimension table…");
            var overlayKeys = OverlayDrawingExecutorCommon.DefaultOverlayDimKeys(run.WedgeType);
            var overlayData = OverlayDrawingExecutorCommon.BuildOverlayPayload(run, drawingData, overlayKeys);

            ds.Rebuild();

            // 4) Apply overlay scales
            Logger.Info("[4/9] Apply overlay view scales…");
            OverlayDrawingExecutorCommon.ApplyOverlayViewScales(ds, nameMap, overlayMag);
            
            // 5) Reposition views via macro
            Logger.Info("[5/9] Reposition all overlay views (macro)…");
            OverlayDrawingExecutorCommon.TryRepositionAllOverlayViews(swApp, ds,run, nameMap);
            var isCkvd = run.WedgeType == WedgeType.CKVD;
            if (isCkvd)
            {
                // 6) Delete Front view when VR == 0
                Logger.Info("[6/9] Delete Front view if VR=0…");
                OverlayDrawingExecutorCommon.DeleteFrontViewIfVrZero(ds, nameMap, ctx);
                
            }
            ds.Rebuild();
            ds.ZoomToSheet();
            
            
            // 7) Plan dims
            Logger.Info("[7/9] Plan overlay dimensions…");
            var (dims, planned) = OverlayDrawingExecutorCommon.PlanOverlayDimensions(ctx, run.WedgeType, plannedDims);

            // 8) Create overlay dimension table
            Logger.Info("[8/9] Create overlay dimension table…");
            OverlayDrawingExecutorCommon.TryCreateOverlayDimTable(swApp, ds, drawingData, overlayData);
            
            // 9) Apply positions + metadata + cleanup + final
            Logger.Info("[9/9] Apply annotation positions…");
            OverlayDrawingExecutorCommon.TryApplyAnnotationPositions(ds, nameMap, run, drawingData, planned);

            Logger.Info("[9b/9] Apply overlay metadata…");
            OverlayDrawingExecutorCommon.TryApplyOverlayMetadata(ds, drawingData, run);

            Logger.Info("[9c/9] Cleanup zero-valued overlay dimensions…");
            OverlayDrawingExecutorCommon.TryCleanupZeroDims(ds, nameMap, ctx, drawingData, dims);

            Logger.Info("[Final-Prep] Draw calibration box + note…");
            OverlayDrawingExecutorCommon.TryCalibrationBoxAndNote(ds, overlayMag, overlayCalUm);

            Logger.Info("[Final] Export overlay drawing (TIFF)…");
            OverlayDrawingExecutorCommon.ExportOverlayTiff(swApp, ds, run);

            Logger.Success($"{bannerLabel} drawing execution completed.");
            /**/

        }
    }
}
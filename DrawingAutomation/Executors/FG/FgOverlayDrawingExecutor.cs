// DrawingAutomation/Executors/FG/FgOverlayDrawingExecutor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Tables;

namespace WAD.Runner.DrawingAutomation.Executors.FG
{
    /// <summary>
    /// CKVD FG Overlay drawing automation.
    /// Uses a dedicated overlay template but the same part template (FG_OVERLAY config).
    /// No breaklines or autoscale; views are placed statically by profile/config.
    /// Overlay magnification + calibration are computed on-the-fly from FL
    /// and kept local to this executor (not stored on WedgeData).
    /// </summary>
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

            Logger.Info("=== WAD ▶ FG/Overlay (static placement, no autoscale) ===");

            var profile = ProfileRegistry.Get(run.Wedge.Subclass, drawingData.DrawingType);
            Logger.Info($"[Profile] Using {profile.ProfileName} for FG/Overlay.");

            // 1) Part automation
            Logger.Info("[1/9] Part Automation…");
            _ = runPartAutomation();

            // 2) Open + relink overlay drawing
            Logger.Info("[2/9] Open + relink overlay drawing…");
            var ds = DrawingExecutorCommon.InitializeAndRelink(swApp, run);

            // 3) Activate overlay sheet via profile
            Logger.Info("[3/9] Activate overlay sheet via profile…");
            var availableSheets = ds.GetSheetNames();
            var sheetName = profile.SheetSelector(availableSheets);
            TryActivateSheet(ds, sheetName);

            // 3b) Delete non-target sheets
            Logger.Info("[3b/9] Delete non-target sheets…");
            var del = ds.DeleteAllSheetsExcept2(sheetName);
            if (!del.Ok)
            {
                if (del.NotDeleted.Count > 0)
                    Logger.Warn($"Some sheets could not be deleted: {string.Join(", ", del.NotDeleted)}");
                else
                    Logger.Warn("DeleteAllSheetsExcept encountered an error (continuing).");
            }
            else if (del.Deleted.Count > 0)
            {
                Logger.Info($"Deleted sheets: {string.Join(", ", del.Deleted)}");
            }
            ds.ZoomToSheet();

            var nameMap = ProfileHelpers.ToNameMap(profile);

            // 4) Compute overlay mag/cal from FL
            Logger.Info("[4/9] Compute overlay magnification/calibration from FL…");
            var ctx = new LayoutContext(run.Wedge, drawingData);
            var (overlayMag, overlayCalUm) = ComputeOverlayMagCalFromFl(ctx);
            Logger.Info($"[Overlay] Using overlayMag={overlayMag}X, overlayCal={overlayCalUm} µm (local to executor).");

            // 4b) Build overlay payload (for dimension table)
            Logger.Info("[4b/9] Build OverlayDrawingPayload from WedgeData + DrawingData…");
            var overlayBuilder = new OverlayDrawingDataBuilder();

            var dimKeys = new[]
            {
                "FL",
                "FR",
                "F",
                "W",
                "BR",
                "GD",
                "GR",
                "B",
                "E",
                "FX",
                "X",
            };

            var overlayData = overlayBuilder.Build(run.Wedge, drawingData, dimKeys);
            Logger.Info($"[OverlayData] Desc='{overlayData.DrawingDescription}', Coining='{overlayData.CoiningText ?? "(none)"}', DimCount={overlayData.Dimensions.Count}");

            var placer = new ViewPlacementService(ds, nameMap);
            var secondary = new SecondaryViewPlacementService(ds, nameMap);

            ds.Rebuild();

            // 7) Apply overlay view scales
            Logger.Info("[7/9] Apply overlay view scales (Front/Side/Top + Detail/Section)…");
            ApplyOverlayViewScales(ds, nameMap, overlayMag);

            // 7b) Reposition all overlay views via macro
            Logger.Info("[7b/9] Reposition all overlay views");
            try
            {
                var overlayMacroPath = GetOverlayMacroPath();

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Detail",
                    sketchName: "ref_point_2",
                    xIn: 6.285,
                    yIn: 2.4,
                    logicalToActual: nameMap);

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Section",
                    sketchName: "ref_point_2",
                    xIn: 3.19,
                    yIn: 2.4,
                    logicalToActual: nameMap);

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Front",
                    sketchName: "ref_point_2",
                    xIn: 0.4,
                    yIn: 0.3,
                    logicalToActual: nameMap);

                SecondaryViewPlacementService.RunMacroForViewIfAvailable(
                    swApp,
                    macroFile: overlayMacroPath,
                    logicalViewName: "Side",
                    sketchName: "ref_point_2",
                    xIn: 3.19,
                    yIn: 0,
                    logicalToActual: nameMap);

                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Reposition-after-scaling step failed (continuing): {ex.Message}");
            }

            // 7c) NEW: delete Front view when VR == 0
            Logger.Info("[7c/9] Check VR and delete Front view if VR=0…");
            DeleteFrontViewIfVrZero(ds, nameMap, ctx);
            ds.Rebuild();
            ds.ZoomToSheet();

            // 8) Plan overlay dimensions
            Logger.Info("[8/9] Plan overlay dimensions (CAD-agnostic)…");
            var diag = new PlannerDiagnostics();
            var dims = DimensionRules.Build(ctx, diag);

            var planned = (plannedDims ?? dims.Select(d => new AnnotationPositioner.Plan
            {
                Id = d.Id,
                View = d.View,
                Key = d.Key,
                PositionMm = d.PositionMm,
                Nominal = d.Nominal
            })).ToList();

            Logger.Info($"[8/9] Planned overlay dims count = {planned.Count}");

            // 8b) Create overlay dimension table
            Logger.Info("[8b/9] Create overlay dimension table from OverlayDrawingPayload…");
            try
            {
                var tableService = new TableService(swApp, ds.Model!);
                if (!TryCreateOverlayDimensionTable(tableService, drawingData, overlayData))
                {
                    Logger.Warn("[Overlay] Dimension table creation skipped or reported failure.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Dimension table step failed (continuing): {ex.Message}");
            }

            // 9) Apply annotation positions
            Logger.Info("[9/9] Apply overlay annotation positions + metadata + cleanup…");
            try
            {
                var pos = new AnnotationPositioner(ds, nameMap);
                pos.Apply(run.Wedge, drawingData, planned);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DimPos/Overlay] Skipped due to error: {ex.Message}");
            }

            // 9b) Apply metadata
            Logger.Info("[9b/9] Apply overlay metadata to drawing properties…");
            try
            {
                MetadataApplier.ApplyOverlay(ds, drawingData, run.Wedge);
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Metadata apply failed (continuing): {ex.Message}");
            }

            // 9c) Cleanup zero-valued overlay dimensions
            Logger.Info("[9c/9] Cleanup zero-valued overlay dimensions based on planning data…");
            try
            {
                AnnotationCleanupService.RemoveZeroDimensionsFromDrawing(ds, nameMap, ctx, drawingData, dims);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Zero-dimension cleanup failed (continuing): {ex.Message}");
            }

            // Final prep: calibration box + note
            Logger.Info("[Final-Prep] Draw overlay calibration box + note in sheet format…");
            try
            {
                ds.DrawCalibrationBoxOnSheetFormat(overlayMag);
                ds.InsertCalibrationBoxNoteBottomRight(overlayCalUm, overlayMag);
                ds.Rebuild();
                ds.ZoomToSheet();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Calibration box/note step failed (continuing): {ex.Message}");
            }

            // Final TIFF export
            Logger.Info("[Final] Export overlay drawing (TIFF)…");
            try
            {
                ds.Rebuild();
                ds.Save();

                string tiffPath;
                if (!string.IsNullOrWhiteSpace(run.OutputTiffPath))
                {
                    tiffPath = Path.GetFullPath(run.OutputTiffPath);
                }
                else
                {
                    var basePath = !string.IsNullOrWhiteSpace(run.OutputPdfPath)
                        ? Path.GetFullPath(run.OutputPdfPath)
                        : Path.GetFullPath(run.ModDrawingPath);

                    tiffPath = Path.ChangeExtension(basePath, ".tif");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(tiffPath)!);

                if (!DrawingExecutorCommon.SaveCurrentSheetAsTiff200Dpi(swApp, ds, tiffPath))
                {
                    Logger.Warn("[Overlay] TIFF export reported failure; see logs above.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] TIFF export step failed (continuing to close): {ex.Message}");
            }
            finally
            {
                try { ds.SaveAndClose(); } catch { }
            }

            Logger.Success("FG/Overlay drawing execution completed.");
        }

        /// <summary>
        /// Computes overlay magnification + calibration in micrometers from FL (mm).
        /// </summary>
        private static (double mag, string calibUm) ComputeOverlayMagCalFromFl(LayoutContext ctx)
        {
            double fl = LayoutMath.Dmm(ctx, "FL");

            if (double.IsNaN(fl) || double.IsInfinity(fl) || fl <= 0.0)
            {
                Logger.Warn("[Overlay] FL missing/invalid; using fallback mag=100, cal=700 µm.");
                return (100.0, "700");
            }

            double mag;
            string calibUm;

            if (fl <= 0.3403) { mag = 400; calibUm = "200.4"; }
            else if (fl <= 0.4572) { mag = 300; calibUm = "399.6"; }
            else if (fl <= 0.6908) { mag = 200; calibUm = "700"; }
            else if (fl <= 1.3766) { mag = 100; calibUm = "700"; }
            else { mag = 100; calibUm = "700"; }

            Logger.Info($"[Overlay] FL={fl:0.####} mm → mag={mag}X, calib={calibUm} µm.");
            return (mag, calibUm);
        }

        /// <summary>
        /// Apply view scales for Front, Side, Top, Detail and Section.
        /// </summary>
        private static void ApplyOverlayViewScales(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            double overlayMag)
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

                double detailSectionScale = GetOverlayViewScaleFromMagnification(overlayMag);

                object viewObj = drawing.GetFirstView();
                while (viewObj is not null)
                {
                    var view = viewObj as View;
                    if (view == null)
                        break;

                    var vName = view.Name ?? string.Empty;

                    if (IsSame(vName, frontName) || IsSame(vName, sideName) || IsSame(vName, topName))
                    {
                        const double oneToOnePointFiveDecimal = 2.0 / 3.0;
                        view.ScaleDecimal = 2;
                        Logger.Info($"[Overlay] Set view '{vName}' scale to approx 1:1.5 (ScaleDecimal={oneToOnePointFiveDecimal:0.####}).");
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

        /// <summary>
        /// Map overlay magnification to detail/section view scale decimal.
        /// </summary>
        private static double GetOverlayViewScaleFromMagnification(double overlayMag)
        {
            int token = (int)Math.Round(overlayMag);
            return token switch
            {
                100 => 60.8,
                200 => 122.7,
                300 => 183.0,
                400 => 246.0,
                _ => 60.8
            };
        }

        private static bool TryCreateOverlayDimensionTable(
            TableService tableService,
            DrawingData drawingData,
            OverlayDrawingPayload overlayData)
        {
            if (drawingData.Tables == null ||
                !drawingData.Tables.TryGetValue("DimTable", out var cfg) ||
                cfg == null ||
                cfg.PositionMm == null ||
                cfg.PositionMm.Length < 2)
            {
                Logger.Warn("[Overlay] DimTable config missing or incomplete; cannot place dimension table.");
                return false;
            }

            var pos = cfg.PositionMm;
            var xMm = pos[0];
            var yMm = pos[1];

            double widthMm = 30.0;
            if (cfg.SizeMm != null && cfg.SizeMm.Length >= 1 && cfg.SizeMm[0] > 0)
            {
                widthMm = cfg.SizeMm[0];
            }

            Logger.Info($"[Overlay] Creating overlay dimension table at ({xMm:0.###}, {yMm:0.###}) mm, width={widthMm:0.###} mm.");

            return tableService.CreateOverlayDimensionTableAt(
                overlayData.Dimensions,
                5,
                121.92,
                widthMm,
                header: "DIMENSIONS");
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

        private static void TryActivateSheet(DrawingService ds, string sheetName)
        {
            try
            {
                var drawing = ds.Drawing ?? throw new InvalidOperationException("No active drawing.");
                drawing.ActivateSheet(sheetName);
                Logger.Info($"Activated sheet: {sheetName}");
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to activate sheet '{sheetName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Delete Front view when VR == 0 (VR in mm from LayoutContext).
        /// Uses profile name map to resolve actual Front view name.
        /// SolidWorks does NOT support drawing.DeleteView(), so we select + DeleteSelection2.
        /// </summary>
        private static void DeleteFrontViewIfVrZero(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            LayoutContext ctx)
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

                var drawing = ds.Drawing;
                var model = ds.Model;
                if (drawing is null || model is null)
                {
                    Logger.Warn("[Overlay] DeleteFrontViewIfVrZero: drawing/model is null.");
                    return;
                }

                // Resolve actual view name from profile (fallback = "Front")
                if (!nameMap.TryGetValue("Front", out var frontName) ||
                    string.IsNullOrWhiteSpace(frontName))
                {
                    frontName = "Front";
                }

                Logger.Info($"[Overlay] VR=0 → deleting Front view '{frontName}'…");

                model.ClearSelection2(true);

                // Select the view as type "DRAWINGVIEW"
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

                // Delete the selected view
                bool ok = model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);

                if (ok)
                {
                    Logger.Info($"[Overlay] Front view '{frontName}' deleted because VR=0.");
                }
                else
                {
                    Logger.Warn($"[Overlay] VR=0 but deletion of Front view '{frontName}' failed.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] DeleteFrontViewIfVrZero failed: {ex.Message}");
            }
        }
    }
}

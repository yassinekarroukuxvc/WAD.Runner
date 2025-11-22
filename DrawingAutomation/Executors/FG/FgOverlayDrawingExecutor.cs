// DrawingAutomation/Executors/FG/FgOverlayDrawingExecutor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData, DrawingType
using WAD.Runner.DataManagement.Domain.Planning;      // LayoutContext, PlannerDiagnostics, DimensionRules, LayoutMath
using WAD.Runner.DataManagement.Domain.Wedge;         // WedgeSubclass, WedgeData
using WAD.Runner.DrawingAutomation.Common;            // DrawingExecutorCommon
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService
using WAD.Runner.DrawingAutomation.Views;             // ViewPlacementService, SecondaryViewPlacementService, AnnotationPositioner, AnnotationCleanupService
using WAD.Runner.DrawingAutomation.Profiles;          // ProfileRegistry, ProfileHelpers
using WAD.Runner.DrawingAutomation.Metadata;          // MetadataApplier
using WAD.Runner.DrawingAutomation.Overlay;           // OverlayDrawingPayload, OverlayDrawingDataBuilder
using WAD.Runner.DrawingAutomation.Tables;            // TableService

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

            // 1) Part automation (FG_OVERLAY config should be handled in PartAutomation via DrawingType=Overlay)
            Logger.Info("[1/9] Part Automation…");
            _ = runPartAutomation();

            // 2) Open + relink drawing (overlay template)
            Logger.Info("[2/9] Open + relink overlay drawing…");
            var ds = DrawingExecutorCommon.InitializeAndRelink(swApp, run);

            // 3) Activate overlay sheet and delete others
            Logger.Info("[3/9] Activate overlay sheet via profile…");
            var availableSheets = ds.GetSheetNames();
            var sheetName = profile.SheetSelector(availableSheets);
            TryActivateSheet(ds, sheetName);

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

            var nameMap = ProfileHelpers.ToNameMap(profile); // logical -> actual SW view name

            // 4) Compute overlay magnification + calibration from FL (local only)
            Logger.Info("[4/9] Compute overlay magnification/calibration from FL…");
            var ctx = new LayoutContext(run.Wedge, drawingData);
            var (overlayMag, overlayCalUm) = ComputeOverlayMagCalFromFl(ctx);
            Logger.Info($"[Overlay] Using overlayMag={overlayMag}X, overlayCal={overlayCalUm} µm (local to executor).");

            // Also build OverlayDrawingPayload (description + coining + dimension rows)
            Logger.Info("[4b/9] Build OverlayDrawingPayload from WedgeData + DrawingData…");
            var overlayBuilder = new OverlayDrawingDataBuilder();

            // specify the dimensions you want to show in the overlay table:
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
            // 5) Place main views (Front / Side / Top)
            Logger.Info("[5/9] Place Front/Side/Top overlay views…");
            var placer = new ViewPlacementService(ds, nameMap);

            // If you want to use config-based positions for main views, uncomment this:
            /*
            placer.Apply("Front", drawingData);
            placer.Apply("Side", drawingData);
            placer.Apply("Top", drawingData);
            */

            // 6) Place Detail/Section overlay views and run macro to build them
            Logger.Info("[6/9] Place Detail/Section overlay views (static) and run macro…");
            var secondary = new SecondaryViewPlacementService(ds, nameMap);

            // Position Front/Side in inches (overlay layout)
            secondary.TrySetViewPositionInches("Front", 1.6, 0.329);
            secondary.TrySetViewPositionInches("Side", 5.2, 0.329);

            // Run overlay macro for Detail & Section using dynamic macro path
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

            ds.Rebuild();

            // 7) Apply overlay view scales:
            //    - Front/Side/Top → approx 1:1.5 (implemented as 2:3 → ScaleDecimal = 2/3)
            //    - Detail/Section → overlayMag → 100/200/300/400 → 60.8/122.7/183/246
            Logger.Info("[7/9] Apply overlay view scales (Front/Side/Top + Detail/Section)…");
            ApplyOverlayViewScales(ds, nameMap, overlayMag);

            // 8) Plan dimensions (CAD-agnostic) for overlay as well
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

            // 8b) Create overlay dimension table using OverlayDrawingPayload + TableService
            Logger.Info("[8b/9] Create overlay dimension table from OverlayDrawingPayload…");
            try
            {
                var tableService = new TableService(swApp, ds.Model);
                if (!TryCreateOverlayDimensionTable(tableService, drawingData, overlayData))
                {
                    Logger.Warn("[Overlay] Dimension table creation skipped or reported failure.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Dimension table step failed (continuing): {ex.Message}");
            }

            // 9) Apply annotation positions in SolidWorks + metadata + cleanup
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

            Logger.Info("[9c/9] Cleanup zero-valued overlay dimensions based on planning data…");
            try
            {
                AnnotationCleanupService.RemoveZeroDimensionsFromDrawing(ds, nameMap, ctx, drawingData, dims);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Overlay] Zero-dimension cleanup failed (continuing): {ex.Message}");
            }

            // Export overlay as TIFF (sheet size, 200 DPI) and close
            Logger.Info("[Final] Export overlay drawing (TIFF)…");
            try
            {
                // Save drawing first
                ds.Save();

                // Decide TIFF output path
                string tiffPath;
                if (!string.IsNullOrWhiteSpace(run.OutputTiffPath))
                {
                    tiffPath = Path.GetFullPath(run.OutputTiffPath);
                }
                else
                {
                    // fallback: same base as PDF or drawing but .tif
                    var basePath = !string.IsNullOrWhiteSpace(run.OutputPdfPath)
                        ? Path.GetFullPath(run.OutputPdfPath)
                        : Path.GetFullPath(run.ModDrawingPath);

                    tiffPath = Path.ChangeExtension(basePath, ".tif");
                }

                // Ensure directory exists
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
        /// Computes overlay magnification + calibration in micrometers from FL (mm),
        /// using the thresholds you specified. Result is local to the executor.
        /// </summary>
        private static (double mag, string calibUm) ComputeOverlayMagCalFromFl(LayoutContext ctx)
        {
            double fl = LayoutMath.Dmm(ctx, "FL"); // FL in millimeters

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
        /// Apply scales:
        /// - Front, Side, Top → approx 1:1.5 (implemented as 2:3 → ScaleDecimal = 2/3)
        /// - Detail, Section   → overlay magnification (100/200/300/400 → 60.8/122.7/183/246)
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

                // Detail/Section scale from overlay magnification
                double detailSectionScale = GetOverlayViewScaleFromMagnification(overlayMag);

                object viewObj = drawing.GetFirstView();
                while (viewObj is not null)
                {
                    var view = viewObj as View;
                    if (view == null)
                        break;

                    var vName = view.Name ?? string.Empty;

                    // FRONT / SIDE / TOP: 1 : 1.5  (≈ 2 : 3)  → ScaleDecimal = 2/3
                    if (IsSame(vName, frontName) || IsSame(vName, sideName) || IsSame(vName, topName))
                    {
                        const double oneToOnePointFiveDecimal = 2.0 / 3.0; // 1 : 1.5 ≈ 2 : 3
                        view.ScaleDecimal = oneToOnePointFiveDecimal;
                        Logger.Info($"[Overlay] Set view '{vName}' scale to approx 1:1.5 (ScaleDecimal={oneToOnePointFiveDecimal:0.####}).");
                    }

                    // DETAIL / SECTION: overlay magnification mapping → decimal scale
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
        /// Map overlay magnification (100/200/300/400) to detail/section view scale decimal.
        /// Same mapping as in the other project:
        /// 100 → 60.8, 200 → 122.7, 300 → 183.0, 400 → 246.0
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

        /// <summary>
        /// Creates the overlay dimension table using the DimTable config (if present)
        /// and the overlayData.Dimensions payload.
        /// </summary>
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

            // 1) First try: copied next to the exe (publish / Debug if you copy content)
            var candidateOutput = Path.Combine(baseDir, "Resources", "Macros", "OverlayMacro.swp");
            candidateOutput = Path.GetFullPath(candidateOutput);

            if (File.Exists(candidateOutput))
            {
                Logger.Info($"[Overlay] Using macro from output folder: {candidateOutput}");
                return candidateOutput;
            }

            // 2) Second try: project root (when running from bin/Debug/net8.0)
            var candidateProject = Path.Combine(baseDir, "..", "..", "..", "Resources", "Macros", "OverlayMacro.swp");
            candidateProject = Path.GetFullPath(candidateProject);

            if (File.Exists(candidateProject))
            {
                Logger.Info($"[Overlay] Using macro from project folder: {candidateProject}");
                return candidateProject;
            }

            // 3) Nothing found – log both attempted paths
            Logger.Warn("[Overlay] OverlayMacro.swp not found. " +
                        $"Tried: '{candidateOutput}' and '{candidateProject}'.");

            // Still return the output-path so the logger in RunMacroForViewIfAvailable
            // shows a consistent 'Expected at:' location.
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
    }
}

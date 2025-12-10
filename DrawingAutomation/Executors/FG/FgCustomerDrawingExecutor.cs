// DrawingAutomation/Executors/FG/FgCustomerDrawingExecutor.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData, DrawingType
using WAD.Runner.DataManagement.Domain.Planning;      // LayoutContext, PlannerDiagnostics, DimensionRules
using WAD.Runner.DataManagement.Domain.Wedge;         // WedgeSubclass, WedgeData
using WAD.Runner.DrawingAutomation.Common;            // DrawingExecutorCommon
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService
using WAD.Runner.DrawingAutomation.Views;             // ViewPlacementService, SecondaryViewPlacementService, ViewAutoScaleService, BreaklineHandler, AnnotationPositioner, AnnotationCleanupService
using WAD.Runner.DrawingAutomation.Profiles;          // ProfileRegistry, ProfileHelpers
using WAD.Runner.DrawingAutomation.Metadata;          // MetadataApplier
using WAD.Runner.DrawingAutomation.Tables;            // TableService (your minimal table helper)

namespace WAD.Runner.DrawingAutomation.Executors.FG
{
    public static class FgCustomerDrawingExecutor
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

            Logger.Info("=== WAD ▶ FG/Customer (Placement + Autoscale) ===");

            var profile = ProfileRegistry.Get(run.Wedge.Subclass, drawingData.DrawingType);

            Logger.Info("[1/11] Part Automation…");
            _ = runPartAutomation();

            Logger.Info("[2/11] Open + relink drawing…");
            var ds = DrawingExecutorCommon.InitializeAndRelink(swApp, run);

            Logger.Info("[3/11] Activate target sheet via profile…");
            var availableSheets = ds.GetSheetNames();
            var sheetName = profile.SheetSelector(availableSheets);
            TryActivateSheet(ds, sheetName);

            Logger.Info("[3b/11] Delete non-target sheets…");
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

            // ---- PLACE VIEWS FIRST -------------------------------------------------
            Logger.Info("[4/11] Place Front/Side/Top views…");
            var placer = new ViewPlacementService(ds, nameMap);
            placer.Apply("Front", drawingData);
            placer.Apply("Side", drawingData);
            placer.Apply("Top", drawingData);

            Logger.Info("[5/11] Place Detail/Section views…");
            var secondary = new SecondaryViewPlacementService(ds, nameMap);
            placer.ApplyDetailAndSection(drawingData);

            // ---- BREAKLINES BEFORE AUTOSCALE --------------------------------------
            EnsureBreaklineGaps(drawingData);

            Logger.Info("[6/11] Breaklines (pre-autoscale)…");
            try
            {
                var model = ds.Model;
                var drawingDoc = ds.Drawing;
                if (model == null || drawingDoc == null)
                {
                    Logger.Warn("Breaklines skipped: model or drawing is null.");
                }
                else
                {
                    void ApplyBl(string logicalViewName)
                    {
                        var view = FindView(ds, logicalViewName, nameMap);
                        if (view == null)
                        {
                            Logger.Warn($"Breakline: view '{logicalViewName}' not found (skipping).");
                            return;
                        }
                        var bl = new BreaklineHandler(view, model);
                        var ok = bl.ApplyBreakline(
                            logicalViewName,
                            drawingData.DrawingType,
                            run.Wedge.Subclass,
                            run.Wedge,
                            drawingData);
                        if (!ok) Logger.Warn($"Breakline: apply failed for '{logicalViewName}'.");
                    }

                    ApplyBl("Front");
                    ApplyBl("Side");
                    ApplyBl("Detail");
                    ApplyBl("Section");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Breaklines step encountered an error (continuing): {ex.Message}");
            }

            // Rebuild so SW updates outlines before we measure for autoscale
            ds.Rebuild();

            // ---- AUTOSCALE AFTER BREAKLINES ---------------------------------------
            Logger.Info("[7/11] Compute unified scale from Front outline (post-breaklines) …");
            var autoscale = new ViewAutoScaleService(ds);
            var policy = ProfileHelpers.ToAutoScalePolicy(profile.Scale);
            autoscale.ApplyUnifiedScaleFromFront(drawingData, policy, nameMap);
            Logger.Info($"[Exec] Scales → Front={drawingData.Views["Front"].Scale:0.###}, Side={drawingData.Views["Side"].Scale:0.###}, Top={drawingData.Views["Top"].Scale:0.###}");

            // Some services only set the scale; re-apply placements to lock target positions at new scales
            Logger.Info("[7b/11] Re-apply placements at new scales…");
            placer.Apply("Front", drawingData);
            placer.Apply("Side", drawingData);
            placer.Apply("Top", drawingData);
            placer.ApplyDetailAndSection(drawingData);
            ds.Rebuild();

            // ---- REPLAN DIMS WITH FINAL SCALES ------------------------------------
            Logger.Info("[8/11] Replan dimensions with final scales…");
            var ctx2 = new LayoutContext(run.Wedge, drawingData);
            var diag2 = new PlannerDiagnostics();
            var dims2 = DimensionRules.Build(ctx2, diag2, run.WedgeType);
            var plannedDimsReplanned = dims2.Select(d => new AnnotationPositioner.Plan
            {
                Id = d.Id,
                View = d.View,
                Key = d.Key,
                PositionMm = d.PositionMm,
                Nominal = d.Nominal
            }).ToList();
            Logger.Info($"[8/11] Replanned dims count = {plannedDimsReplanned.Count}");

            // ---- APPLY ANNOTATIONS -------------------------------------------------
            Logger.Info("[9/11] Apply planned annotation positions…");
            try
            {
                var pos = new AnnotationPositioner(ds, nameMap);
                pos.Apply(run.Wedge, drawingData, plannedDimsReplanned);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DimPos] Skipped due to error: {ex.Message}");
            }

            // ---- APPLY METADATA TO DRAWING PROPERTIES -----------------------------
            Logger.Info("[10/11] Apply metadata to drawing properties…");
            try
            {
                MetadataApplier.Apply(ds, drawingData, run.Wedge);
                ds.Rebuild(); // ensure title block notes refresh
            }
            catch (Exception ex)
            {
                Logger.Warn($"Metadata apply failed (continuing): {ex.Message}");
            }

            // ---- TABLES (simple create) -------------------------------------------
            Logger.Info("[10b/11] Create tables…");
            try
            {
                var swModel = ds.Model as ModelDoc2;
                if (swModel == null || ds.Drawing == null)
                {
                    Logger.Warn("Tables skipped: model or drawing is null.");
                }
                else
                {
                    var tableSvc = new TableService(swApp, swModel);

                    try
                    {
                        if (drawingData.Tables?.ContainsKey("DimTable") == true)
                            tableSvc.CreateDimensionTable(run.Wedge, drawingData, tableId: "DimTable", header: "DIMENSIONS");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"CreateDimensionTable failed: {ex.Message}");
                    }

                    try
                    {
                        if (drawingData.Tables?.ContainsKey("HowToOrder") == true)
                            tableSvc.CreateHowToOrderTable(run.Wedge, drawingData, headerText: "HOW TO ORDER", tableId: "HowToOrder");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"CreateHowToOrderTable failed: {ex.Message}");
                    }

                    try
                    {
                        if (drawingData.Tables?.ContainsKey("LabelAs") == true)
                            tableSvc.CreateLabelAsTable(drawingData, tableId: "LabelAs");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"CreateLabelAsTable failed: {ex.Message}");
                    }

                    try
                    {
                        if (drawingData.Tables?.ContainsKey("Polish") == true)
                            tableSvc.CreatePolishTable(drawingData, tableId: "Polish");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"CreatePolishTable failed: {ex.Message}");
                    }

                    ds.Rebuild(); // reflect tables before export
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Tables step encountered an error (continuing): {ex.Message}");
            }

            // ---- DATA-DRIVEN ZERO DIMENSION CLEANUP -------------------------------
            Logger.Info("[10c/11] Cleanup zero-valued dimensions based on planning data…");
            try
            {
                AnnotationCleanupService.RemoveZeroDimensionsFromDrawing(ds, nameMap, ctx2, drawingData, dims2);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Zero-dimension cleanup failed (continuing): {ex.Message}");
            }

            // ---- EXPORT ------------------------------------------------------------
            Logger.Info("[11/11] Export & finalize…");
            DrawingExecutorCommon.FinalizeProduction(swApp, ds, run.OutputPdfPath);
            Logger.Success("FG/Customer drawing execution completed.");
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

        private static void EnsureBreaklineGaps(DrawingData dd)
        {
            if (dd?.Views == null) return;
            if (dd.Views.TryGetValue("Front", out var front) && front != null) front.Params["breakline_gap_mm"] = 2.0;
            if (dd.Views.TryGetValue("Side", out var side) && side != null) side.Params["breakline_gap_mm"] = 2.0;
            if (dd.Views.TryGetValue("Detail", out var detail) && detail != null) detail.Params["breakline_gap_mm"] = 50.0;
            if (dd.Views.TryGetValue("Section", out var section) && section != null) section.Params["breakline_gap_mm"] = 50.0;
        }

        private static View? FindView(DrawingService ds, string logicalName, IDictionary<string, string> nameMap)
        {
            try
            {
                if (ds?.Drawing is not DrawingDoc dd) return null;
                string actualName = logicalName;
                if (nameMap != null && nameMap.TryGetValue(logicalName, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
                    actualName = mapped;

                View v = dd.IGetFirstView();
                if (v == null) return null;
                v = v.IGetNextView(); // skip sheet
                int guard = 0;
                while (v != null && guard++ < 512)
                {
                    try
                    {
                        var vn = v.Name;
                        if (!string.IsNullOrWhiteSpace(vn) &&
                            string.Equals(vn, actualName, StringComparison.OrdinalIgnoreCase))
                            return v;
                    }
                    catch { }
                    v = v.IGetNextView();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"FindView('{logicalName}') failed: {ex.Message}");
            }
            return null;
        }
    }
}

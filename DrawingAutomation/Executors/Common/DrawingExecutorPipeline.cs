// DrawingAutomation/Executors/Common/DrawingExecutorPipeline.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Profiles;

// ✅ Cleanup runners
using WAD.Runner.DrawingAutomation.Rules.COB;

namespace WAD.Runner.DrawingAutomation.Executors.Common
{
    public static class DrawingExecutorPipeline
    {
        public sealed class Hooks
        {
            /// <summary>Select profile (usually wedgeType switch -> ProfileRegistry.GetXxx)</summary>
            public required Func<DrawingRun, DrawingData, DrawingProfile> SelectProfile { get; init; }

            /// <summary>Optional: run after placements and BEFORE breaklines (rare)</summary>
            public Action<DrawingService, IDictionary<string, string>, DrawingRun, DrawingData>? AfterPlaceViews { get; init; }

            /// <summary>
            /// Optional: run after metadata/tables/zero-cleanup, before export.
            /// Keep this for custom executor-only logic, NOT for wedge-type cleanup (pipeline handles wedge-type cleanup now).
            /// </summary>
            public Action<DrawingService, IDictionary<string, string>, DrawingRun, DrawingData>? BeforeExport { get; init; }

            /// <summary>Optional: override export/finalize logic</summary>
            public Action<SldWorks, DrawingService, DrawingRun>? Export { get; init; }

            /// <summary>Optional: customize breakline gaps (defaults used if null)</summary>
            public Action<DrawingData>? EnsureBreaklineGaps { get; init; }

            /// <summary>Controls logging header line.</summary>
            public string Banner { get; init; } = "=== WAD ▶ Drawing Execution ===";
        }

        public static void Run(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            Func<object?> runPartAutomation,
            Hooks hooks)
            => Run(swApp, run, drawingData, runPartAutomation, plannedDims: null, hooks);

        public static void Run(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            Func<object?> runPartAutomation,
            IEnumerable<AnnotationPositioner.Plan>? plannedDims,
            Hooks hooks)
        {
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
            if (runPartAutomation is null) throw new ArgumentNullException(nameof(runPartAutomation));
            if (hooks is null) throw new ArgumentNullException(nameof(hooks));
            if (hooks.SelectProfile is null) throw new ArgumentNullException(nameof(hooks.SelectProfile));

            Logger.Info(hooks.Banner);

            // 0) Profile
            var profile = hooks.SelectProfile(run, drawingData);
            Logger.Info($"[Profile] Using profile '{profile.ProfileName}' for {run.WedgeType}/{run.Wedge.Subclass}/{drawingData.DrawingType}");

            // 1) Part automation
            Logger.Info("[1/11] Part Automation…");
            _ = runPartAutomation();

            // 2) Open + relink
            Logger.Info("[2/11] Open + relink drawing…");
            var ds = DrawingExecutorCommon.InitializeAndRelink(swApp, run);

            // 3) Activate target sheet + delete others
            Logger.Info("[3/11] Activate target sheet via profile…");
            var sheetName = profile.SheetSelector(ds.GetSheetNames());
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

            // 4) Place primary views
            Logger.Info("[4/11] Place Front/Side/Top views…");
            var placer = new ViewPlacementService(ds, nameMap);
            placer.Apply("Front", drawingData);
            placer.Apply("Side", drawingData);
            placer.Apply("Top", drawingData);

            // 5) Place secondary views
            Logger.Info("[5/11] Place Detail/Section views…");
            _ = new SecondaryViewPlacementService(ds, nameMap);
            placer.ApplyDetailAndSection(drawingData);

            // 5x) Hook: after placements
            hooks.AfterPlaceViews?.Invoke(ds, nameMap, run, drawingData);

            // Breakline gaps
            if (hooks.EnsureBreaklineGaps != null) hooks.EnsureBreaklineGaps(drawingData);
            else EnsureBreaklineGaps_Default(drawingData);

            // 6) Breaklines
            Logger.Info("[6/11] Breaklines (pre-autoscale)…");
            TryApplyBreaklines(ds, nameMap, run, drawingData);

            ds.Rebuild();

            // 7) Autoscale
            Logger.Info("[7/11] Compute unified scale from Front outline (post-breaklines) …");
            var autoscale = new ViewAutoScaleService(ds);
            var policy = ProfileHelpers.ToAutoScalePolicy(profile.Scale);
            autoscale.ApplyUnifiedScaleFromFront(drawingData, policy, nameMap);
            Logger.Info(
                $"[Exec] Scales → Front={drawingData.Views["Front"].Scale:0.###}, " +
                $"Side={drawingData.Views["Side"].Scale:0.###}, Top={drawingData.Views["Top"].Scale:0.###}");

            // 7b) Re-apply placements at new scales
            Logger.Info("[7b/11] Re-apply placements at new scales…");
            placer.Apply("Front", drawingData);
            placer.Apply("Side", drawingData);
            placer.Apply("Top", drawingData);
            placer.ApplyDetailAndSection(drawingData);
            ds.Rebuild();

            // 8) Replan dims
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

            // 9) Apply annotation positions
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

            // 10) Metadata
            Logger.Info("[10/11] Apply metadata to drawing properties…");
            try
            {
                MetadataApplier.Apply(ds, drawingData, run.Wedge);
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Metadata apply failed (continuing): {ex.Message}");
            }

            // 10b) Tables
            Logger.Info("[10b/11] Create tables…");
            TryCreateTables(swApp, ds, run, drawingData);

            // 10c) Zero-dim cleanup
            Logger.Info("[10c/11] Cleanup zero-valued dimensions based on planning data…");
            try
            {
                AnnotationCleanupService.RemoveZeroDimensionsFromDrawing(ds, nameMap, ctx2, drawingData, dims2);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Zero-dimension cleanup failed (continuing): {ex.Message}");
            }

            // Optional hook: executor-specific custom logic
            hooks.BeforeExport?.Invoke(ds, nameMap, run, drawingData);

            // ✅ WedgeType-specific cleanup (centralized here)
            Logger.Info("[10d/11] Wedge cleanup runners…");
            TryRunCleanupRunners(ds, nameMap, run, drawingData);

            // 11) Export
            Logger.Info("[11/11] Export & finalize…");
            if (hooks.Export != null)
            {
                hooks.Export(swApp, ds, run);
            }
            else
            {
                DrawingExecutorCommon.FinalizeProduction(swApp, ds, run.OutputPdfPath);
            }
        }

        // ------------------------------------------------------------
        // Cleanup dispatch (for now: only COB)
        // ------------------------------------------------------------

        private static void TryRunCleanupRunners(
            DrawingService ds,
            IDictionary<string, string> nameMap,
            DrawingRun run,
            DrawingData drawingData)
        {
            try
            {
                switch (run.WedgeType)
                {
                    case WedgeType.COB:
                        // ✅ Run COB cleanup
                        new CobAnnotationCleanupRunner().TryApply(ds, nameMap, run, drawingData, activateEachView: true);
                        ds.Rebuild();
                        break;

                    case WedgeType.CKVD:
                    case WedgeType.OSG7:
                    default:
                        // For now: no cleanup
                        Logger.Info($"[Cleanup] No cleanup runner registered for {run.WedgeType} (skipping).");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Cleanup] Failed (continuing): {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // Shared internals
        // ------------------------------------------------------------

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

        private static void EnsureBreaklineGaps_Default(DrawingData dd)
        {
            if (dd?.Views == null) return;
            if (dd.Views.TryGetValue("Front", out var front) && front != null) front.Params["breakline_gap_mm"] = 2.0;
            if (dd.Views.TryGetValue("Side", out var side) && side != null) side.Params["breakline_gap_mm"] = 2.0;
            if (dd.Views.TryGetValue("Detail", out var detail) && detail != null) detail.Params["breakline_gap_mm"] = 50.0;
            if (dd.Views.TryGetValue("Section", out var section) && section != null) section.Params["breakline_gap_mm"] = 50.0;
        }

        private static void TryApplyBreaklines(DrawingService ds, IDictionary<string, string> nameMap, DrawingRun run, DrawingData drawingData)
        {
            try
            {
                var model = ds.Model;
                var drawingDoc = ds.Drawing;
                if (model == null || drawingDoc == null)
                {
                    Logger.Warn("Breaklines skipped: model or drawing is null.");
                    return;
                }

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
            catch (Exception ex)
            {
                Logger.Warn($"Breaklines step encountered an error (continuing): {ex.Message}");
            }
        }

        private static void TryCreateTables(SldWorks swApp, DrawingService ds, DrawingRun run, DrawingData drawingData)
        {
            try
            {
                var swModel = ds.Model as ModelDoc2;
                if (swModel == null || ds.Drawing == null)
                {
                    Logger.Warn("Tables skipped: model or drawing is null.");
                    return;
                }

                var tableSvc = new TableService(swApp, swModel);

                try
                {
                    if (drawingData.Tables?.ContainsKey("DimTable") == true)
                        tableSvc.CreateDimensionTable(
                            run.Wedge,
                            drawingData,
                            wedgeType: run.WedgeType,
                            tableId: "DimTable",
                            header: "DIMENSIONS");
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

                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Tables step encountered an error (continuing): {ex.Message}");
            }
        }

        private static View? FindView(DrawingService ds, string logicalName, IDictionary<string, string> nameMap)
        {
            try
            {
                if (ds?.Drawing is not DrawingDoc dd) return null;

                string actualName = logicalName;
                if (nameMap != null &&
                    nameMap.TryGetValue(logicalName, out var mapped) &&
                    !string.IsNullOrWhiteSpace(mapped))
                {
                    actualName = mapped;
                }

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
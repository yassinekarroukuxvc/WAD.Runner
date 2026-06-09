// DrawingAutomation/Executors/Common/DrawingExecutorPipeline.cs
using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Executors.Common
{
    /// <summary>
    /// Pipeline utilities ONLY (no wedge-type decisions).
    /// Executors orchestrate ordering + wedge-specific steps.
    /// </summary>
    public static class DrawingExecutorPipeline
    {
        // ------------------------------------------------------------
        // State DTO used by executors
        // ------------------------------------------------------------

        public sealed class PipelineState
        {
            public required DrawingService Ds { get; init; }
            public required IDictionary<string, string> NameMap { get; init; }
            public required ViewPlacementService Placer { get; init; }
            public required string ActiveSheet { get; init; }
        }

        public sealed class ReplanResult
        {
            public required LayoutContext Context { get; init; }
            public required IReadOnlyList<DimensionSpec> Dims { get; init; }
            public required IReadOnlyList<AnnotationPositioner.Plan> Plans { get; init; }
        }

        // ------------------------------------------------------------
        // Entry-ish helpers
        // ------------------------------------------------------------

        public static void LogBanner(string banner) => Logger.Info(banner);

        public static void RunPartAutomation(Func<object?> runPartAutomation)
        {
            if (runPartAutomation is null) throw new ArgumentNullException(nameof(runPartAutomation));
            Logger.Info("[1/11] Part Automation…");
            _ = runPartAutomation();
        }

        public static PipelineState OpenRelinkAndPrepare(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            DrawingProfile profile)
        {
            if (swApp is null) throw new ArgumentNullException(nameof(swApp));
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
            if (profile is null) throw new ArgumentNullException(nameof(profile));

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
            var placer = new ViewPlacementService(ds, nameMap);

            return new PipelineState
            {
                Ds = ds,
                NameMap = nameMap,
                Placer = placer,
                ActiveSheet = sheetName
            };
        }

        public static void PlaceAllViews(PipelineState st, DrawingData drawingData)
        {
            if (st is null) throw new ArgumentNullException(nameof(st));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            // 4) Place primary views
            Logger.Info("[4/11] Place Front/Side/Top views…");
            st.Placer.Apply("Front", drawingData);
            st.Placer.Apply("Side", drawingData);
            st.Placer.Apply("Top", drawingData);

            // 5) Place secondary views
            Logger.Info("[5/11] Place Detail/Section views…");
            _ = new SecondaryViewPlacementService(st.Ds, st.NameMap);
            st.Placer.ApplyDetailAndSection(drawingData);
        }

        public static void EnsureBreaklineGaps_Default(DrawingData dd)
        {
            if (dd?.Views == null) return;

            SetDefaultGap(dd, "Front", 2.0);
            SetDefaultGap(dd, "Side", 2.0);
            SetDefaultGap(dd, "Detail", 50.0);
            SetDefaultGap(dd, "Section", 50.0);
        }

        private static void SetDefaultGap(DrawingData dd, string viewName, double defaultMm)
        {
            if (!dd.Views.TryGetValue(viewName, out var view) || view == null)
                return;

            if (!view.Params.ContainsKey("breakline_gap_mm"))
                view.Params["breakline_gap_mm"] = defaultMm;
        }

        public static void ApplyBreaklines(PipelineState st, DrawingRun run, DrawingData drawingData)
        {
            if (st is null) throw new ArgumentNullException(nameof(st));
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            Logger.Info("[6/11] Breaklines (pre-autoscale)…");
            TryApplyBreaklines(st.Ds, st.NameMap, run, drawingData);
            st.Ds.Rebuild();
        }

        public static void AutoScaleAndReapplyPlacements(PipelineState st, DrawingData drawingData, DrawingProfile profile)
        {
            if (st is null) throw new ArgumentNullException(nameof(st));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
            if (profile is null) throw new ArgumentNullException(nameof(profile));

            // 7) Autoscale
            Logger.Info("[7/11] Compute unified scale from Front outline (post-breaklines) …");
            var autoscale = new ViewAutoScaleService(st.Ds);
            var policy = ProfileHelpers.ToAutoScalePolicy(profile.Scale);
            autoscale.ApplyUnifiedScaleFromFront(drawingData, policy, st.NameMap);

            Logger.Info(
                $"[Exec] Scales → Front={drawingData.Views["Front"].Scale:0.###}, " +
                $"Side={drawingData.Views["Side"].Scale:0.###}, Top={drawingData.Views["Top"].Scale:0.###}");

            // 7b) Re-apply placements at new scales
            Logger.Info("[7b/11] Re-apply placements at new scales…");
            st.Placer.Apply("Front", drawingData);
            st.Placer.Apply("Side", drawingData);
            st.Placer.Apply("Top", drawingData);
            st.Placer.ApplyDetailAndSection(drawingData);

            st.Ds.Rebuild();
        }

        public static ReplanResult ReplanDimensions(DrawingRun run, DrawingData drawingData)
        {
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            Logger.Info("[8/11] Replan dimensions with final scales…");
            var ctx2 = new LayoutContext(run.Wedge, drawingData);
            var diag2 = new PlannerDiagnostics();
            var dims2 = DimensionRules.Build(ctx2, diag2, run.WedgeType).ToList();

            DumpDimensionSpecs(dims2, title: "[8/11] dims2 (DimensionSpec)");

            var plannedDims = dims2.Select(d => new AnnotationPositioner.Plan
            {
                Id = d.Id,
                View = d.View,
                Key = d.Key,
                PositionMm = d.PositionMm,
                Nominal = d.Nominal
            }).ToList();

            Logger.Blue($"[8/11] Replanned dims count = {plannedDims.Count}");

            return new ReplanResult
            {
                Context = ctx2,
                Dims = dims2,
                Plans = plannedDims
            };
        }

        public static void RunAnnotationCleanup(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        DrawingRun run,
        DrawingData drawingData)
        {
            // Per-wedge annotation cleanup (COB / UTUS / FP / CKVD / OSG7 / future types)
            var runner = AnnotationCleanupRunnerFactory.TryGet(run.WedgeType);
            if (runner == null)
            {
                Logger.Info($"[Pipeline] No annotation cleanup runner registered for {run.WedgeType} (skipping).");
                return;
            }

            try
            {
                Logger.Info($"[Pipeline] Running {run.WedgeType} annotation cleanup…");
                runner.TryApply(ds, nameMap, run, drawingData, activateEachView: true);
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Pipeline] Annotation cleanup failed (continuing): {ex.Message}");
            }
        }

        public static void ApplyAnnotationPositions(
            PipelineState st,
            DrawingRun run,
            DrawingData drawingData,
            IEnumerable<AnnotationPositioner.Plan> plannedDims)
        {
            if (st is null) throw new ArgumentNullException(nameof(st));
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
            if (plannedDims is null) throw new ArgumentNullException(nameof(plannedDims));

            Logger.Info("[9/11] Apply planned annotation positions…");
            try
            {
                var pos = new AnnotationPositioner(st.Ds, st.NameMap);
                pos.Apply(run.Wedge, drawingData, plannedDims.ToList());
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DimPos] Skipped due to error: {ex.Message}");
            }
        }

        public static void ApplyMetadata(DrawingService ds, DrawingData drawingData, WedgeData wedge)
        {
            if (ds is null) throw new ArgumentNullException(nameof(ds));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            Logger.Info("[10/11] Apply metadata to drawing properties…");
            try
            {
                MetadataApplier.Apply(ds, drawingData, wedge);
                ds.Rebuild();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Metadata apply failed (continuing): {ex.Message}");
            }
        }

        public static void CreateTables(SldWorks swApp, DrawingService ds, DrawingRun run, DrawingData drawingData)
        {
            Logger.Info("[10b/11] Create tables…");
            TryCreateTables(swApp, ds, run, drawingData);
        }

        public static void ExportDefault(SldWorks swApp, DrawingService ds, string outputPdfPath)
        {
            Logger.Info("[11/11] Export & finalize…");
            DrawingExecutorCommon.FinalizeProduction(swApp, ds, outputPdfPath);
        }

        // ------------------------------------------------------------
        // Internals (mostly unchanged)
        // ------------------------------------------------------------

        private static void DumpDimensionSpecs(IEnumerable<DimensionSpec> dims, string title)
        {
            if (dims is null)
            {
                Logger.Warn($"{title}: (null)");
                return;
            }

            var list = dims.ToList();
            Logger.Info($"{title}: count={list.Count}");

            foreach (var grp in list
                .OrderBy(d => d.View)
                .ThenBy(d => d.Key.Value)
                .GroupBy(d => d.View, StringComparer.OrdinalIgnoreCase))
            {
                Logger.Info($"  ── View: {grp.Key} ({grp.Count()} dims)");
                foreach (var d in grp)
                {
                    var x = (d.PositionMm != null && d.PositionMm.Length > 0) ? d.PositionMm[0] : double.NaN;
                    var y = (d.PositionMm != null && d.PositionMm.Length > 1) ? d.PositionMm[1] : double.NaN;

                    var nom = d.Nominal;
                    string nomStr = nom.Unit switch
                    {
                        UnitKind.Millimeter => $"{nom.Value:0.####} mm",
                        UnitKind.Degree => $"{nom.Value:0.####} deg",
                        _ => $"{nom.Value:0.####} {nom.Unit}"
                    };

                    Logger.Info($"    • {d.Key.Value,-10}  pos=({x:0.##},{y:0.##}) mm  axis={d.Axis}  nominal={nomStr}  id={d.Id}");
                }
            }
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
// DrawingAutomation/Executors/OverlayDrawingExecutor.cs
using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Executors.Common;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Executors;

/// <summary>
/// Unified Overlay drawing executor (replaces FgOverlayDrawingExecutor and
/// PgbOverlayDrawingExecutor — their bodies were ~90% identical).
///
/// The only real difference between FG and PGB overlay was:
///   FG  : calls TryBindOverlayViewConfigurations (non-CKVD path)
///   PGB : calls TryBindReferencedConfigsForPgb (CKVD path, now folded in here)
/// Both differences are now handled by subclass-aware logic within this single executor.
///
/// Pipeline order:
///   1  Part automation
///   2  Open + relink + overlay sheet prep
///   2b View config binding (subclass-aware)
///   3  Compute overlay magnification / calibration
///   3b Build overlay payload
///   4  Apply overlay view scales
///   5  Reposition all overlay views
///   6  (CKVD only) Delete Front view when VR == 0
///   7  Plan overlay dimensions
///   8  Create overlay dimension table
///   9  Apply annotation positions
///   9b Apply overlay metadata
///   9c Clean up zero-valued overlay dimensions
///   F1 Draw calibration box + note
///   F2 Export TIFF
/// </summary>
public static class OverlayDrawingExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation,
        IEnumerable<AnnotationPositioner.Plan>? plannedDims = null)
    {
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
        if (runPartAutomation is null) throw new ArgumentNullException(nameof(runPartAutomation));

        var label = $"{run.Wedge.Subclass}/Overlay | {run.WedgeType}";
        Logger.Info($"=== WAD ▶ {label} (executor-owned pipeline) ===");

        // Profile resolution — uses DrawingProfileResolver (fixes the FP/GetUtus bug
        // that was also present in OverlayDrawingExecutorCommon.OpenRelinkAndPrepareOverlaySheet)
        var profile = DrawingProfileResolver.Resolve(run, drawingData);
        Logger.Info($"[Profile] Using {profile.ProfileName} for {label}.");

        bool isCkvd = run.WedgeType == WedgeType.CKVD;

        // ── 1) Part automation ────────────────────────────────────────────
        Logger.Info("[1/9] Part Automation…");
        _ = runPartAutomation();

        // ── 2) Open + relink + overlay sheet prep ─────────────────────────
        Logger.Info("[2/9] Open + relink + sheet prep…");
        var ds = OverlayDrawingExecutorCommon.OpenRelinkAndPrepareOverlaySheet(
            swApp, run, drawingData, out var nameMap);

        // ── 2b) View config binding (subclass-aware) ──────────────────────
        BindViewConfigurations(ds, run, drawingData, nameMap, isCkvd);

        // ── 3) Compute overlay mag/cal + payload ──────────────────────────
        Logger.Info("[3/9] Compute overlay magnification/calibration…");
        var (ctx, overlayMag, overlayCalUm) = OverlayDrawingExecutorCommon.ComputeOverlayMagCal(run, drawingData);

        Logger.Info("[3b/9] Build overlay payload for dimension table…");
        var overlayKeys = OverlayDrawingExecutorCommon.DefaultOverlayDimKeys(run.WedgeType);
        var overlayData = OverlayDrawingExecutorCommon.BuildOverlayPayload(run, drawingData, overlayKeys);

        ds.Rebuild();

        // ── 4) Apply overlay view scales ──────────────────────────────────
        Logger.Info("[4/9] Apply overlay view scales…");
        OverlayDrawingExecutorCommon.ApplyOverlayViewScales(ds, nameMap, overlayMag);

        // ── 5) Reposition all overlay views ───────────────────────────────
        Logger.Info("[5/9] Reposition all overlay views…");
        OverlayDrawingExecutorCommon.TryRepositionAllOverlayViews(swApp, ds, run, nameMap);

        // ── 6) CKVD only: delete Front view when VR == 0 ─────────────────
        if (isCkvd)
        {
            Logger.Info("[6/9] Delete Front view if VR=0…");
            OverlayDrawingExecutorCommon.DeleteFrontViewIfVrZero(ds, nameMap, ctx);
        }

        ds.Rebuild();
        ds.ZoomToSheet();

        // ── 7) Plan overlay dimensions ────────────────────────────────────
        Logger.Info("[7/9] Plan overlay dimensions…");
        var (dims, planned) = OverlayDrawingExecutorCommon.PlanOverlayDimensions(ctx, run.WedgeType, plannedDims);

        // ── 8) Create overlay dimension table ─────────────────────────────
        Logger.Info("[8/9] Create overlay dimension table…");
        OverlayDrawingExecutorCommon.TryCreateOverlayDimTable(swApp, ds, drawingData, overlayData);

        // ── 9) Apply annotation positions ─────────────────────────────────
        Logger.Info("[9/9] Apply annotation positions…");
        OverlayDrawingExecutorCommon.TryApplyAnnotationPositions(ds, nameMap, run, drawingData, planned);

        Logger.Info("[9b/9] Apply overlay metadata…");
        OverlayDrawingExecutorCommon.TryApplyOverlayMetadata(ds, drawingData, run);

        Logger.Info("[9c/9] Cleanup zero-valued overlay dimensions…");
        OverlayDrawingExecutorCommon.TryCleanupZeroDims(ds, nameMap, ctx, drawingData, dims);

        // ── Final: calibration box + export ───────────────────────────────
        Logger.Info("[Final-Prep] Draw calibration box + note…");
        OverlayDrawingExecutorCommon.TryCalibrationBoxAndNote(ds, overlayMag, overlayCalUm);

        Logger.Info("[Final] Export overlay drawing (TIFF)…");
        OverlayDrawingExecutorCommon.ExportOverlayTiff(swApp, ds, run);

        Logger.Success($"{label} drawing execution completed.");
    }

    // ────────────────────────────────────────────────────────────────────
    // View config binding — the only real difference between FG and PGB
    // overlay was which binding path was taken.
    // ────────────────────────────────────────────────────────────────────

    private static void BindViewConfigurations(
        DrawingService ds,
        DrawingRun run,
        DrawingData drawingData,
        IDictionary<string, string> nameMap,
        bool isCkvd)
    {
        if (isCkvd)
        {
            // PGB overlay: Front/Side/Top reference the Production config,
            // Detail/Section reference the Overlay config.
            Logger.Info("[2b/9] Bind views to PGB production/overlay configs…");
            TryBindPgbOverlayConfigs(ds, nameMap, run, drawingData);
        }
        else
        {
            // FG overlay (COB/UTUS/FP): bind all views using DrawingViewConfigBinder
            Logger.Info("[2b/9] Bind overlay view configurations (non-CKVD)…");
            OverlayDrawingExecutorCommon.TryBindOverlayViewConfigurations(ds, run, nameMap);
        }
    }

    /// <summary>
    /// PGB overlay config binding: Front/Side/Top → Production config,
    /// Detail/Section → Overlay config.
    /// Extracted from the old PgbOverlayDrawingExecutor.TryBindReferencedConfigsForPgbOverlay.
    /// </summary>
    private static void TryBindPgbOverlayConfigs(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        DrawingRun run,
        DrawingData drawingData)
    {
        try
        {
            if (ds.Model is not ModelDoc2 model) return;

            nameMap.TryGetValue("Front", out var frontViewName);
            nameMap.TryGetValue("Side", out var sideViewName);
            nameMap.TryGetValue("Top", out var topViewName);
            nameMap.TryGetValue("Detail", out var detailViewName);
            nameMap.TryGetValue("Section", out var sectionViewName);

            var subclass = run.Wedge.Subclass;
            var prodType = DrawingType.Production;
            var overlayType = drawingData.DrawingType;

            // Front/Side/Top show the Production config geometry
            if (!string.IsNullOrWhiteSpace(frontViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, frontViewName, subclass, prodType);
            if (!string.IsNullOrWhiteSpace(sideViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, sideViewName, subclass, prodType);
            if (!string.IsNullOrWhiteSpace(topViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, topViewName, subclass, prodType);

            // Detail/Section show the Overlay config geometry
            if (!string.IsNullOrWhiteSpace(detailViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, detailViewName, subclass, overlayType);
            if (!string.IsNullOrWhiteSpace(sectionViewName))
                DrawingViewConfigBinder.SetReferencedConfigurationForView(model, sectionViewName, subclass, overlayType);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Overlay/PGB] Config binding failed (continuing): {ex.Message}");
        }
    }
}

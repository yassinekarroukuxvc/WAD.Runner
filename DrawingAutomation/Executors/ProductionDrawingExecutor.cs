// DrawingAutomation/Executors/ProductionDrawingExecutor.cs
using System;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Executors.Common;
using WAD.Runner.DrawingAutomation.Profiles;

namespace WAD.Runner.DrawingAutomation.Executors;

/// <summary>
/// Unified Production drawing executor (replaces the former FgProductionDrawingExecutor
/// and PgbProductionDrawingExecutor — their bodies were identical).
///
/// Handles: FG Production, PGB Production, Customer drawings for all wedge types.
///
/// Pipeline order:
///   1  Part automation
///   2  Open + relink + sheet selection
///   3  Activate target sheet + delete others
///   4  Place Front / Side / Top views
///   5  Place Detail / Section views
///   6  Apply breaklines
///   7  Autoscale + re-apply placements
///   8  Replan dimensions at final scale
///   9  Delete annotations (CKVD: zero-valued / COB-UTUS-FP: by-fullname plan)
///  10  Reposition remaining annotations
///  11  Apply metadata
///  12  Create tables
///  13  Export (PDF)
/// </summary>
public static class ProductionDrawingExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation)
    {
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
        if (runPartAutomation is null) throw new ArgumentNullException(nameof(runPartAutomation));

        DrawingExecutorPipeline.LogBanner(
            $"=== WAD ▶ {run.Wedge.Subclass}/{drawingData.DrawingType} | {run.WedgeType} ===");

        // 0) Profile — one call replaces the 5-branch switch that lived in every executor
        var profile = DrawingProfileResolver.Resolve(run, drawingData);
        Logger.Info($"[Profile] Using '{profile.ProfileName}' for {run.WedgeType}/{run.Wedge.Subclass}/{drawingData.DrawingType}");

        // 1) Part automation
        DrawingExecutorPipeline.RunPartAutomation(runPartAutomation);

        // 2-3) Open / relink / sheet
        var st = DrawingExecutorPipeline.OpenRelinkAndPrepare(swApp, run, drawingData, profile);

        // 4-5) View placement
        DrawingExecutorPipeline.PlaceAllViews(st, drawingData);

        // Default breakline gaps
        DrawingExecutorPipeline.EnsureBreaklineGaps_Default(drawingData);

        // 6) Breaklines
        DrawingExecutorPipeline.ApplyBreaklines(st, run, drawingData);

        // 7) Autoscale + re-placement
        DrawingExecutorPipeline.AutoScaleAndReapplyPlacements(st, drawingData, profile);

        // 8) Replan dimensions at final scale
        var replanned = DrawingExecutorPipeline.ReplanDimensions(run, drawingData);

        // 9) Delete annotations BEFORE repositioning
        DrawingExecutorPipeline.RunAnnotationCleanup(st.Ds, st.NameMap, run, drawingData);

        // 10) Reposition what remains
        DrawingExecutorPipeline.ApplyAnnotationPositions(st, run, drawingData, replanned.Plans);

        // 11) Metadata
        DrawingExecutorPipeline.ApplyMetadata(st.Ds, drawingData, run.Wedge);

        // 12) Tables
        DrawingExecutorPipeline.CreateTables(swApp, st.Ds, run, drawingData);

        // 13) Export
        DrawingExecutorPipeline.ExportDefault(swApp, st.Ds, run.OutputPdfPath);
    }
}

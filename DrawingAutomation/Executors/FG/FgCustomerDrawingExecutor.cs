// DrawingAutomation/Executors/FG/FgCustomerDrawingExecutor.cs
using System;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Executors.Common;
using WAD.Runner.DrawingAutomation.Profiles;

namespace WAD.Runner.DrawingAutomation.Executors.FG
{
    public static class FgCustomerDrawingExecutor
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

            DrawingExecutorPipeline.LogBanner("=== WAD ▶ FG/Customer (Placement + Autoscale) ===");

            // 0) Profile
            var profile = run.WedgeType switch
            {
                WedgeType.COB => ProfileRegistry.GetCob(run.Wedge.Subclass, drawingData.DrawingType),
                WedgeType.OSG7 => ProfileRegistry.GetOsg7(run.Wedge.Subclass, drawingData.DrawingType),
                _ => ProfileRegistry.GetCkvd(run.Wedge.Subclass, drawingData.DrawingType)
            };

            Logger.Info($"[Profile] Using profile '{profile.ProfileName}' for {run.WedgeType}/{run.Wedge.Subclass}/{drawingData.DrawingType}");

            // 1) Part
            DrawingExecutorPipeline.RunPartAutomation(runPartAutomation);

            // 2-3) Open/relink/sheet
            var st = DrawingExecutorPipeline.OpenRelinkAndPrepare(swApp, run, drawingData, profile);

            // 4-5) Place
            DrawingExecutorPipeline.PlaceAllViews(st, drawingData);

            // Breakline gaps (default)
            DrawingExecutorPipeline.EnsureBreaklineGaps_Default(drawingData);

            // 6) Breaklines
            DrawingExecutorPipeline.ApplyBreaklines(st, run, drawingData);

            // 7) Autoscale + re-place
            DrawingExecutorPipeline.AutoScaleAndReapplyPlacements(st, drawingData, profile);

            // 8) Replan
            var replanned = DrawingExecutorPipeline.ReplanDimensions(run, drawingData);

            // ✅ CHANGE: delete BEFORE reposition
            // - CKVD only: delete zero-valued annotations
            if (run.WedgeType == WedgeType.CKVD)
                DrawingExecutorPipeline.DeleteZeroValuedAnnotations(st.Ds, st.NameMap, replanned.Context, drawingData, replanned.Dims);

            // - COB cleanup if applicable
            if (run.WedgeType == WedgeType.COB)
                DrawingExecutorPipeline.RunCobAnnotationCleanup(st.Ds, st.NameMap, run, drawingData);

            // 9) Reposition what remains
            DrawingExecutorPipeline.ApplyAnnotationPositions(st, run, drawingData, replanned.Plans);

            // 10) Metadata
            DrawingExecutorPipeline.ApplyMetadata(st.Ds, drawingData, run.Wedge);

            // 10b) Tables
            DrawingExecutorPipeline.CreateTables(swApp, st.Ds, run, drawingData);

            // 11) Export (keeping your current default finalize behavior)
            DrawingExecutorPipeline.ExportDefault(swApp, st.Ds, run.OutputPdfPath);
        }
    }
}
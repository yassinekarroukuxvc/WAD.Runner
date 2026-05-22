using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Executors.PGB;

/// <summary>
/// Backwards-compatible entry point for PGB overlay drawings.
/// The implementation lives in the unified <see cref="OverlayDrawingExecutor"/>.
/// </summary>
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
        => WAD.Runner.DrawingAutomation.Executors.OverlayDrawingExecutor.Run(swApp, run, drawingData, runPartAutomation, plannedDims);
}

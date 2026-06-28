using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Execution;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Executors;


public static class OverlayDrawingExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation,
        IEnumerable<AnnotationPositioner.Plan>? plannedDims = null)
        => DrawingAutomationExecutor.RunOverlay(swApp, run, drawingData, runPartAutomation, plannedDims);
}

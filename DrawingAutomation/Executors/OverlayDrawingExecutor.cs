using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Execution;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Executors;

/// <summary>
/// Compatibility entry point used by Program.cs.
/// The actual workflow is implemented by DrawingAutomationExecutor and pipelines.
/// </summary>
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

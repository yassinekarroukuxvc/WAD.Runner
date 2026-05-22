using System;

using SolidWorks.Interop.sldworks;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Execution;

namespace WAD.Runner.DrawingAutomation.Executors;

/// <summary>
/// Compatibility entry point used by Program.cs.
/// The actual workflow is implemented by DrawingAutomationExecutor and pipelines.
/// </summary>
public static class ProductionDrawingExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation)
        => DrawingAutomationExecutor.RunProduction(swApp, run, drawingData, runPartAutomation);
}

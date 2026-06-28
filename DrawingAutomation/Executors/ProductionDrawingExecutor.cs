using System;

using SolidWorks.Interop.sldworks;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Execution;

namespace WAD.Runner.DrawingAutomation.Executors;


public static class ProductionDrawingExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation)
        => DrawingAutomationExecutor.RunProduction(swApp, run, drawingData, runPartAutomation);
}

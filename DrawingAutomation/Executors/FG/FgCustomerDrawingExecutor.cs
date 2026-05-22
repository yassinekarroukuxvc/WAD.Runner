using System;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Drawing;

namespace WAD.Runner.DrawingAutomation.Executors.FG;

/// <summary>
/// Backwards-compatible entry point for FG customer drawings.
/// The implementation lives in the unified <see cref="ProductionDrawingExecutor"/>.
/// </summary>
public static class FgCustomerDrawingExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation)
        => WAD.Runner.DrawingAutomation.Executors.ProductionDrawingExecutor.Run(swApp, run, drawingData, runPartAutomation);
}

// DrawingAutomation/Executors/FG/FgProductionDrawingExecutor.cs
using System;
using SolidWorks.Interop.sldworks;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Executors.Common; // ✅ DrawingExecutorPipeline
using WAD.Runner.DrawingAutomation.Profiles;         // ✅ ProfileRegistry

namespace WAD.Runner.DrawingAutomation.Executors.FG
{
    public static class FgProductionDrawingExecutor
    {
        public static void Run(
            SldWorks swApp,
            DrawingRun run,
            DrawingData drawingData,
            Func<object?> runPartAutomation)
        {
            DrawingExecutorPipeline.Run(
                swApp,
                run,
                drawingData,
                runPartAutomation,
                new DrawingExecutorPipeline.Hooks
                {
                    Banner = "=== WAD ▶ FG/Production (Placement + Autoscale) ===",

                    // Profile selection is the only FG/Production-specific thing left here.
                    // Cleanup is now centralized in DrawingExecutorPipeline (COB only for now).
                    SelectProfile = (r, dd) => r.WedgeType switch
                    {
                        WedgeType.COB => ProfileRegistry.GetCob(r.Wedge.Subclass, dd.DrawingType),
                        WedgeType.OSG7 => ProfileRegistry.GetOsg7(r.Wedge.Subclass, dd.DrawingType),
                        _ => ProfileRegistry.GetCkvd(r.Wedge.Subclass, dd.DrawingType)
                    }
                });
        }
    }
}
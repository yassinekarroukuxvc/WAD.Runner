using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Execution;

/// <summary>
/// Single drawing automation entry point.
/// </summary>
public static class DrawingAutomationExecutor
{
    public static void Run(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation,
        IEnumerable<AnnotationPositioner.Plan>? plannedOverlayDimensions = null)
    {
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
        if (runPartAutomation is null) throw new ArgumentNullException(nameof(runPartAutomation));

        DrawingRunValidator.Validate(run);
        _ = DrawingWedgeBehaviorCatalog.Get(run.WedgeType);

        var profile = DrawingProfileResolver.Resolve(run, drawingData);
        Logger.Info($"[DrawingAutomation] Profile = {profile.ProfileName} ({run.WedgeType}/{run.Wedge.Subclass}/{drawingData.DrawingType})");

        var context = new DrawingAutomationContext(
            swApp,
            run,
            drawingData,
            profile,
            runPartAutomation,
            plannedOverlayDimensions);

        new DrawingPipelineRouter().Run(context);
    }

    public static void RunProduction(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation)
        => Run(swApp, run, drawingData, runPartAutomation, plannedOverlayDimensions: null);

    public static void RunOverlay(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        Func<object?> runPartAutomation,
        IEnumerable<AnnotationPositioner.Plan>? plannedOverlayDimensions = null)
        => Run(swApp, run, drawingData, runPartAutomation, plannedOverlayDimensions);
}

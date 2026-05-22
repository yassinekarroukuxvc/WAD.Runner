using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Rules.Common;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Rules.Annotation;

/// <summary>
/// Applies drawing annotation visibility/deletion rules only.
/// This is not model feature handling.
/// </summary>
public static class DrawingAnnotationCleanupStep
{
    public static void Run(
        DrawingService drawingService,
        IDictionary<string, string> viewNames,
        DrawingRun run,
        DrawingData drawingData)
    {
        if (drawingService is null) throw new ArgumentNullException(nameof(drawingService));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        var runner = AnnotationCleanupRunnerFactory.TryGet(run.WedgeType);
        if (runner is null)
        {
            Logger.Info($"[AnnotationCleanup] No cleanup runner registered for {run.WedgeType}; skipping.");
            return;
        }

        try
        {
            Logger.Info($"[AnnotationCleanup] Applying {run.WedgeType} drawing annotation rules...");
            runner.TryApply(drawingService, viewNames, run, drawingData, activateEachView: true);
            drawingService.Rebuild();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[AnnotationCleanup] Failed, continuing: {ex.Message}");
        }
    }
}

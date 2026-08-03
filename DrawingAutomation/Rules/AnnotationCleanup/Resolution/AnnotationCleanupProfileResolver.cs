using System;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class AnnotationCleanupProfileResolver
{
    public static AnnotationCleanupProfile Resolve(
        DrawingRun run,
        DrawingData drawingData)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        return DrawingWedgeModuleRegistry
            .Get(run.WedgeType)
            .ResolveAnnotationProfile(
                run.Wedge.Subclass,
                drawingData.DrawingType);
    }
}

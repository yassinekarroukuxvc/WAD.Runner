using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Core;

/// <summary>
/// Runtime context for one drawing automation run.
/// </summary>
public sealed class DrawingAutomationContext
{
    public DrawingAutomationContext(
        SldWorks swApp,
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile,
        Func<object?> runPartAutomation,
        IEnumerable<AnnotationPositioner.Plan>? plannedOverlayDimensions = null)
    {
        SwApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
        Run = run ?? throw new ArgumentNullException(nameof(run));
        DrawingData = drawingData ?? throw new ArgumentNullException(nameof(drawingData));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        RunPartAutomation = runPartAutomation ?? throw new ArgumentNullException(nameof(runPartAutomation));
        PlannedOverlayDimensions = plannedOverlayDimensions;
    }

    public SldWorks SwApp { get; }
    public DrawingRun Run { get; }
    public DrawingData DrawingData { get; }
    public DrawingProfile Profile { get; }
    public Func<object?> RunPartAutomation { get; }
    public IEnumerable<AnnotationPositioner.Plan>? PlannedOverlayDimensions { get; }
}

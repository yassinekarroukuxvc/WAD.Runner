using System.Collections.Generic;

using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Execution;

public sealed class DrawingPipelineState
{
    public required DrawingService DrawingService { get; init; }
    public required IDictionary<string, string> ViewNames { get; init; }
    public required ViewPlacementService ViewPlacement { get; init; }
    public required string ActiveSheetName { get; init; }
}

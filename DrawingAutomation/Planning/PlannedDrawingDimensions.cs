using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Planning;

public sealed class PlannedDrawingDimensions
{
    public required LayoutContext Context { get; init; }
    public required IReadOnlyList<DimensionSpec> Dimensions { get; init; }
    public required IReadOnlyList<AnnotationPositioner.Plan> AnnotationPlans { get; init; }
}

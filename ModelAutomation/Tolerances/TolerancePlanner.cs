using System;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules;

namespace WAD.Runner.ModelAutomation.Tolerances;

/// <summary>
/// Central dispatcher that now delegates to the wedge profile registry.
/// </summary>
public sealed class TolerancePlanner
{
    public TolerancePlan Build(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        return WedgeAutomationProfileRegistry
            .For(wedgeType)
            .ToleranceRules
            .Build(wedge, drawingType, subclass);
    }
}

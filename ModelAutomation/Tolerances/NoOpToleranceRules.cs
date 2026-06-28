using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Tolerances;

public sealed class NoOpToleranceRules : IToleranceRuleSet
{
    public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        => TolerancePlan.Empty;
}

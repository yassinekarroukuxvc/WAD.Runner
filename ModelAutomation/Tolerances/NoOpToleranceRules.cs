using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Tolerances;

/// <summary>
/// Safe no-op fallback for wedge types that do not need tolerance planning yet.
/// </summary>
public sealed class NoOpToleranceRules : IToleranceRuleSet
{
    public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
        => TolerancePlan.Empty;
}

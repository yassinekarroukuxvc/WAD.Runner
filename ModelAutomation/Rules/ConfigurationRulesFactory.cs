using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules;

/// <summary>
/// Backward-compatible facade that now delegates to the centralized profile registry.
/// Keeping this class means existing callers do not break.
/// </summary>
public static class ConfigurationRulesFactory
{
    public static IModelConfigurationRules For(WedgeType wedgeType)
        => WedgeAutomationProfileRegistry.For(wedgeType).ConfigurationRules;
}

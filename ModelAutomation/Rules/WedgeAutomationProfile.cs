using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules;

/// <summary>
/// Central bundle for all ModelAutomation rule services for one wedge type.
/// This lets the rest of the code ask for one profile instead of repeating
/// wedge-type switches in multiple places.
/// </summary>
public sealed record WedgeAutomationProfile(
    IModelConfigurationRules ConfigurationRules,
    IFeatureRuleSet FeatureRules,
    IEquationInputNormalizer EquationNormalizer,
    IToleranceRuleSet ToleranceRules
);

using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules;

/// <summary>
/// One profile describes one wedge family. The orchestrator only knows profiles,
/// not wedge-specific conditions.
/// </summary>
public sealed record WedgeAutomationProfile(
    WedgeType WedgeType,
    string Name,
    IModelConfigurationRules ConfigurationRules,
    IFeatureRuleSet FeatureRules,
    IEquationPlanner EquationPlanner,
    IToleranceRuleSet ToleranceRules,
    IReadOnlyCollection<string> PostRebuildSuppressions
);

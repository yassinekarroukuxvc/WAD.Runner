using System;
using System.Collections.Generic;
using System.Linq;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules;

public enum ToggleApplicationMode
{
    ActiveConfiguration,
    AllConfigurations,
    ExplicitSteps
}

public sealed record FeatureToggleStep(string ConfigurationName, string? FeatureRuleProfile = null);

public sealed record ConfigurationPlan(
    string ConfigurationName,
    ToggleApplicationMode ToggleMode,
    IReadOnlyList<FeatureToggleStep>? ToggleSteps = null);

public static class ConfigurationPlanFactory
{
    public static ConfigurationPlan ForActive(string configurationName)
        => new(NormalizeName(configurationName), ToggleApplicationMode.ActiveConfiguration);

    public static ConfigurationPlan ForAll(string configurationName)
        => new(NormalizeName(configurationName), ToggleApplicationMode.AllConfigurations);

    public static ConfigurationPlan ForExplicit(string finalConfigurationName, IEnumerable<FeatureToggleStep>? steps)
        => new(NormalizeName(finalConfigurationName), ToggleApplicationMode.ExplicitSteps, NormalizeSteps(finalConfigurationName, steps));

    public static FeatureToggleStep Step(string configurationName, string? featureRuleProfile = null)
        => new(NormalizeName(configurationName), string.IsNullOrWhiteSpace(featureRuleProfile) ? null : featureRuleProfile.Trim());

    public static bool HasExplicitSteps(IReadOnlyList<FeatureToggleStep>? steps) => steps is { Count: > 0 };

    private static IReadOnlyList<FeatureToggleStep> NormalizeSteps(string finalConfigurationName, IEnumerable<FeatureToggleStep>? steps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<FeatureToggleStep>();

        void Add(FeatureToggleStep? step)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.ConfigurationName)) return;
            var config = NormalizeName(step.ConfigurationName);
            if (!seen.Add(config)) return;
            result.Add(new FeatureToggleStep(config, string.IsNullOrWhiteSpace(step.FeatureRuleProfile) ? null : step.FeatureRuleProfile.Trim()));
        }

        if (steps is not null)
            foreach (var step in steps) Add(step);

        Add(new FeatureToggleStep(finalConfigurationName));
        return result;
    }

    private static string NormalizeName(string value)
        => string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
}

public interface IModelConfigurationRules
{
    ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null);
}

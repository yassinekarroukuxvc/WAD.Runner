using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.M;

public sealed class MConfigurationRules : IModelConfigurationRules
{
    private const string DefaultConfiguration = "Default";
    private const string LeftViewConfiguration = "left_view";
    private const string RightViewConfiguration = "right_view";

    public ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
    {
        if (drawingType != DrawingType.Overlay)
        {
            return Build(
                DefaultConfiguration,
                explicitToggleSteps,
                false,
                "non-overlay");
        }

        if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
        {
            return Build(
                RightViewConfiguration,
                explicitToggleSteps,
                true,
                "overlay explicit override");
        }

        return Build(
            RightViewConfiguration,
            BuildOverlaySteps(),
            true,
            "overlay left/right multi-config");
    }

    private static IReadOnlyList<FeatureToggleStep> BuildOverlaySteps()
    {
        return new[]
        {
            ConfigurationPlanFactory.Step(LeftViewConfiguration, LeftViewConfiguration),
            ConfigurationPlanFactory.Step(RightViewConfiguration, RightViewConfiguration)
        };
    }

    private static ConfigurationPlan Build(
        string finalConfig,
        IReadOnlyList<FeatureToggleStep>? steps,
        bool explicitSteps,
        string reason)
    {
        var plan = explicitSteps || ConfigurationPlanFactory.HasExplicitSteps(steps)
            ? ConfigurationPlanFactory.ForExplicit(finalConfig, steps)
            : ConfigurationPlanFactory.ForActive(finalConfig);

        Logger.Info(
            $"[MConfigurationRules] {reason} -> " +
            $"{plan.ConfigurationName}/{plan.ToggleMode}");

        return plan;
    }
}
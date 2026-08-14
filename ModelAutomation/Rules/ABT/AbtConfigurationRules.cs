using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Rules.ABT;

public sealed class AbtConfigurationRules : IModelConfigurationRules
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

        if (ConfigurationPlanFactory.HasExplicitSteps(
                explicitToggleSteps))
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
            ConfigurationPlanFactory.Step(
                LeftViewConfiguration,
                LeftViewConfiguration),

            ConfigurationPlanFactory.Step(
                RightViewConfiguration,
                RightViewConfiguration)
        };
    }

    private static ConfigurationPlan Build(
        string finalConfig,
        IReadOnlyList<FeatureToggleStep>? steps,
        bool explicitSteps,
        string reason)
    {
        var plan =
            explicitSteps ||
            ConfigurationPlanFactory.HasExplicitSteps(steps)
                ? ConfigurationPlanFactory.ForExplicit(
                    finalConfig,
                    steps)
                : ConfigurationPlanFactory.ForActive(
                    finalConfig);

        Logger.Info(
            $"[AbtConfigurationRules] {reason} -> " +
            $"{plan.ConfigurationName}/{plan.ToggleMode}");

        return plan;
    }
}
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

public sealed class CkvdConfigurationRules : IModelConfigurationRules
{
    public ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
    {
        if (drawingType != DrawingType.Overlay)
        {
            return Build(
                "Default",
                explicitToggleSteps,
                false,
                "non-overlay");
        }

        var facts =
            wedge is null
                ? null
                : new WedgeFacts(wedge);

        var finalConfig =
            subclass == WedgeSubclass.PGB
                ? ResolvePgbOverlayConfig(facts)
                : ResolveFgOverlayConfig(facts);

        if (ConfigurationPlanFactory.HasExplicitSteps(
                explicitToggleSteps))
        {
            return Build(
                finalConfig,
                explicitToggleSteps,
                true,
                "explicit override");
        }

        return Build(
            finalConfig,
            BuildOverlaySteps(),
            true,
            subclass == WedgeSubclass.PGB
                ? "PGB overlay multi-config"
                : "FG overlay multi-config");
    }

    private static string ResolveFgOverlayConfig(
        WedgeFacts? facts)
    {
        var hasVw =
            facts?.HasPositive("VW") == true;

        var hasVr =
            facts?.HasPositive("VR") == true ||
            facts?.HasPositive("VRR") == true;

        if (!hasVw && !hasVr)
            return "Overlay";

        if (hasVw && hasVr)
            return "Overlay_non_std_cut";

        return "Default";
    }

    private static string ResolvePgbOverlayConfig(
        WedgeFacts? facts)
    {
        var hasVw =
            facts?.HasPositive("VW") == true;

        var hasVr =
            facts?.HasPositive("VR") == true ||
            facts?.HasPositive("VRR") == true;

        if (!hasVw && !hasVr)
            return "Overlay";

        if (hasVw && hasVr)
            return "Overlay_non_std_cut";

        return "Default";
    }

    private static IReadOnlyList<FeatureToggleStep>
        BuildOverlaySteps()
    {
        return new[]
        {
            ConfigurationPlanFactory.Step(
                "Default",
                "Default"),

            ConfigurationPlanFactory.Step(
                "Overlay",
                "Overlay"),

            ConfigurationPlanFactory.Step(
                "Overlay_non_std_cut",
                "Overlay_non_std_cut")
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
            $"[CkvdConfigurationRules] {reason} -> " +
            $"{plan.ConfigurationName}/{plan.ToggleMode}");

        return plan;
    }
}
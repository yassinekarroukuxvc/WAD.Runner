using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

public abstract class CobLikeConfigurationRulesBase : IModelConfigurationRules
{
    private readonly string _logPrefix;

    protected CobLikeConfigurationRulesBase(string logPrefix)
    {
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix)
            ? nameof(CobLikeConfigurationRulesBase)
            : logPrefix;
    }

    public ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
    {
        if (drawingType != DrawingType.Overlay)
            return Build("Default", explicitToggleSteps, false, "non-overlay");

        var facts = wedge is null ? null : new WedgeFacts(wedge);

        var finalConfig = subclass == WedgeSubclass.PGB
            ? ResolvePgbOverlayConfig(facts)
            : ResolveFgOverlayConfig(facts);

        if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            return Build(finalConfig, explicitToggleSteps, true, "explicit override");

        return Build(
            finalConfig,
            BuildOverlaySteps(),
            true,
            subclass == WedgeSubclass.PGB
                ? "PGB overlay multi-config"
                : "FG overlay multi-config");
    }

    protected virtual string ResolveFgOverlayConfig(WedgeFacts? facts)
    {
        var hasVw = facts?.HasPositive("VW") == true;
        var hasVr = facts?.HasPositive("VR") == true;

        if (!hasVw && !hasVr) return "std_cut";
        if (hasVw && hasVr) return "non_std_cut";

        return "Default";
    }

    protected virtual string ResolvePgbOverlayConfig(WedgeFacts? facts)
    {
        var hasVw = facts?.HasPositive("VW") == true;
        var hasVr = facts?.HasPositive("VR") == true;

        if (!hasVw && !hasVr) return "std_cut";
        if (hasVw && hasVr) return "non_std_cut";

        return "Default";
    }

    protected virtual IReadOnlyList<FeatureToggleStep> BuildOverlaySteps()
        => new[]
        {
            ConfigurationPlanFactory.Step("Default", "default_config"),
            ConfigurationPlanFactory.Step("std_cut", "std_cut"),
            ConfigurationPlanFactory.Step("non_std_cut", "non_std_cut")
        };

    private ConfigurationPlan Build(
        string finalConfig,
        IReadOnlyList<FeatureToggleStep>? steps,
        bool explicitSteps,
        string reason)
    {
        var plan = explicitSteps || ConfigurationPlanFactory.HasExplicitSteps(steps)
            ? ConfigurationPlanFactory.ForExplicit(finalConfig, steps)
            : ConfigurationPlanFactory.ForActive(finalConfig);

        Logger.Info($"[{_logPrefix}] {reason} -> {plan.ConfigurationName}/{plan.ToggleMode}");
        return plan;
    }
}

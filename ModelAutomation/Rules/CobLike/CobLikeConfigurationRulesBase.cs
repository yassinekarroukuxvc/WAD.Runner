using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared COB/FP/UTUS configuration policy.
/// Only this class decides which SolidWorks configurations must be touched.
/// </summary>
public abstract class CobLikeConfigurationRulesBase : IModelConfigurationRules
{
    private readonly string _logPrefix;

    protected CobLikeConfigurationRulesBase(string logPrefix)
    {
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? nameof(CobLikeConfigurationRulesBase) : logPrefix;
    }

    public ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
    {
        if (drawingType != DrawingType.Overlay)
            return Build("Default", explicitToggleSteps, false, "non-overlay");

        if (subclass == WedgeSubclass.PGB)
            return Build("std_cut", explicitToggleSteps, false, "PGB overlay standard cut");

        var facts = wedge is null ? null : new WedgeFacts(wedge);
        var finalConfig = ResolveFgOverlayConfig(facts);

        if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            return Build(finalConfig, explicitToggleSteps, true, "explicit override");

        return Build(finalConfig, BuildFgOverlaySteps(), true, "FG overlay multi-config");
    }

    protected virtual string ResolveFgOverlayConfig(WedgeFacts? facts)
    {
        var hasVw = facts?.HasPositive("VW") == true;
        var hasVr = facts?.HasPositive("VR") == true;

        if (!hasVw && !hasVr) return "std_cut";
        if (hasVw && hasVr) return "non_std_cut";
        return "Default";
    }

    protected virtual IReadOnlyList<FeatureToggleStep> BuildFgOverlaySteps()
        => new[]
        {
            ConfigurationPlanFactory.Step("Default", "default_config"),
            ConfigurationPlanFactory.Step("std_cut", "std_cut"),
            ConfigurationPlanFactory.Step("non_std_cut", "non_std_cut")
        };

    private ConfigurationPlan Build(string finalConfig, IReadOnlyList<FeatureToggleStep>? steps, bool explicitSteps, string reason)
    {
        var plan = explicitSteps || ConfigurationPlanFactory.HasExplicitSteps(steps)
            ? ConfigurationPlanFactory.ForExplicit(finalConfig, steps)
            : ConfigurationPlanFactory.ForActive(finalConfig);

        Logger.Info($"[{_logPrefix}] {reason} -> {plan.ConfigurationName}/{plan.ToggleMode}");
        return plan;
    }
}

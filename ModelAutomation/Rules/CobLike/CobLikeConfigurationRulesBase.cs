using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared configuration-selection logic for COB-like wedges.
/// Keeps COB / FP / UTUS aligned and leaves only the wedge-type identity in the
/// concrete classes.
/// </summary>
public abstract class CobLikeConfigurationRulesBase : IModelConfigurationRules
{
    private readonly string _logPrefix;

    protected CobLikeConfigurationRulesBase(string logPrefix)
    {
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "CobLikeConfigRules" : logPrefix;
    }

    public ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
    {
        var facts = wedge is null ? null : new CobLikeRuleFacts(wedge);
        string config;

        if (drawingType != DrawingType.Overlay)
        {
            config = "Default";

            if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            {
                Logger.Info($"[{_logPrefix}] Non-overlay → Default / ExplicitSteps");
                return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
            }

            Logger.Info($"[{_logPrefix}] Non-overlay → Default / ActiveConfiguration");
            return ConfigurationPlanFactory.ForActive(config);
        }

        if (subclass == WedgeSubclass.PGB)
        {
            config = "Default";

            if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            {
                Logger.Info($"[{_logPrefix}] Overlay + PGB → Default / ExplicitSteps");
                return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
            }

            Logger.Info($"[{_logPrefix}] Overlay + PGB → Default / AllConfigurations");
            return ConfigurationPlanFactory.ForActive(config);
        }

        bool hasVw = facts?.HasVw == true;
        bool hasVr = facts?.HasVr == true;

        config = ResolveOverlayFgConfig(facts);

        if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
        {
            Logger.Info($"[{_logPrefix}] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / ExplicitSteps (override)");
            return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
        }

        Logger.Info($"[{_logPrefix}] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / ExplicitSteps");
        return ConfigurationPlanFactory.ForExplicit(config, BuildOverlayFgSteps());
    }

    /// <summary>
    /// Allows a specific COB-like wedge type to override only the FG overlay
    /// configuration selection without duplicating the whole Resolve() method.
    /// </summary>
    protected virtual string ResolveOverlayFgConfig(CobLikeRuleFacts? facts)
    {
        bool hasVw = facts?.HasVw == true;
        bool hasVr = facts?.HasVr == true;

        if (!hasVw && !hasVr)
            return "std_cut";

        if (hasVw && hasVr)
            return "non_std_cut";

        return "Default";
    }

    protected virtual IReadOnlyList<FeatureToggleStep> BuildOverlayFgSteps()
        => new[]
        {
            ConfigurationPlanFactory.Step("Default", "default_config"),
            ConfigurationPlanFactory.Step("std_cut", "std_cut"),
            ConfigurationPlanFactory.Step("non_std_cut", "non_std_cut")
        };
}
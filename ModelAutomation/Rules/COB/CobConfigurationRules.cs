// ModelAutomation/Rules/COB/CobConfigurationRules.cs
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.COB
{
    /// <summary>
    /// Configuration rules for COB wedges.
    ///
    /// Non-overlay (Production / Customer):
    ///   - Always "Default", toggles applied to the active configuration only.
    ///
    /// Overlay + PGB:
    ///   - Always "Default", toggles applied to all configurations.
    ///
    /// Overlay + FG:
    ///   - Final active config stays data-driven:
    ///       * no VW and no VR   → "std_cut"
    ///       * VW > 0 and VR > 0 → "non_std_cut"
    ///       * otherwise         → "Default"
    ///   - Toggle application uses explicit per-config steps so each overlay
    ///     configuration can receive its own feature-rule set.
    ///
    /// If explicit toggle steps are supplied by the caller, they take precedence.
    /// </summary>
    public sealed class CobConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(
            WedgeSubclass subclass,
            DrawingType drawingType,
            WedgeData? wedge,
            IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
        {
            string config;

            // 1. Non-overlay logic
            if (drawingType != DrawingType.Overlay)
            {
                config = "Default";

                if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
                {
                    Logger.Info("[CobConfigRules] Non-overlay → Default / ExplicitSteps");
                    return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
                }

                Logger.Info("[CobConfigRules] Non-overlay → Default / ActiveConfiguration");
                return ConfigurationPlanFactory.ForActive(config);
            }

            // 2. Overlay + PGB logic
            if (subclass == WedgeSubclass.PGB)
            {
                config = "Default";

                if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
                {
                    Logger.Info("[CobConfigRules] Overlay + PGB → Default / ExplicitSteps");
                    return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
                }

                Logger.Info("[CobConfigRules] Overlay + PGB → Default / AllConfigurations");
                return ConfigurationPlanFactory.ForAll(config);
            }

            // 3. Overlay + FG logic
            bool hasVw = IsDimPositive(wedge, "VW");
            bool hasVr = IsDimPositive(wedge, "VR");

            if (!hasVw && !hasVr)
            {
                config = "std_cut";
            }
            else if (hasVw && hasVr)
            {
                config = "non_std_cut";
            }
            else
            {
                config = "Default";
            }

            if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            {
                Logger.Info($"[CobConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / ExplicitSteps (override)");
                return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
            }

            Logger.Info($"[CobConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / ExplicitSteps");
            return ConfigurationPlanFactory.ForExplicit(config, BuildOverlayFgSteps());
        }

        private static IReadOnlyList<FeatureToggleStep> BuildOverlayFgSteps()
            => new[]
            {
                ConfigurationPlanFactory.Step("Default", "default_config"),
                ConfigurationPlanFactory.Step("std_cut", "std_cut"),
                ConfigurationPlanFactory.Step("non_std_cut", "non_std_cut")
            };

        private static bool IsDimPositive(WedgeData? wedge, string key)
        {
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null) return false;
            return dim.Nominal.Value > 0m;
        }
    }
}
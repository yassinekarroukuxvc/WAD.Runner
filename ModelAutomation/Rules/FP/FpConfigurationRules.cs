// ModelAutomation/Rules/FP/FpConfigurationRules.cs
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.FP
{
    /// <summary>
    /// Configuration rules for FP wedges.
    /// FP uses the same configuration selection logic as COB.
    ///
    /// Overlay + FG uses explicit per-config steps by default so each overlay
    /// configuration can receive its own feature-rule set.
    /// If explicit toggle steps are supplied by the caller, they take precedence.
    /// </summary>
    public sealed class FpConfigurationRules : IModelConfigurationRules
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
                    Logger.Info("[FpConfigRules] Non-overlay → Default / ExplicitSteps");
                    return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
                }

                Logger.Info("[FpConfigRules] Non-overlay → Default / ActiveConfiguration");
                return ConfigurationPlanFactory.ForActive(config);
            }

            // 2. Overlay + PGB logic
            if (subclass == WedgeSubclass.PGB)
            {
                config = "Default";

                if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
                {
                    Logger.Info("[FpConfigRules] Overlay + PGB → Default / ExplicitSteps");
                    return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
                }

                Logger.Info("[FpConfigRules] Overlay + PGB → Default / AllConfigurations");
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
                Logger.Info($"[FpConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / ExplicitSteps (override)");
                return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
            }

            Logger.Info($"[FpConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / ExplicitSteps");
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
using System.Collections.Generic;
// ModelAutomation/Rules/DefaultConfigurationRules.cs
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// Safe fallback configuration rules for unimplemented or future wedge types.
    /// Returns "Default" and applies toggles across all configurations to avoid
    /// stale state in any config.
    ///
    /// If explicit toggle steps are supplied, they take precedence and toggles
    /// are applied only to those configurations.
    /// </summary>
    public sealed class DefaultConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(
            WedgeSubclass subclass,
            DrawingType drawingType,
            WedgeData? wedge,
            IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
        {
            const string config = "Default";

            if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            {
                Logger.Info("[DefaultConfigRules] Unknown wedge type → Default / ExplicitSteps");
                return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
            }

            Logger.Info("[DefaultConfigRules] Unknown wedge type → Default / AllConfigurations");
            return ConfigurationPlanFactory.ForAll(config);
        }
    }
}

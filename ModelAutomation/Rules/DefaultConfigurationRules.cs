using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules;

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
            Logger.Info("[DefaultConfigurationRules] Default / ExplicitSteps");
            return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
        }

        Logger.Info("[DefaultConfigurationRules] Default / ActiveConfiguration");
        return ConfigurationPlanFactory.ForActive(config);
    }
}

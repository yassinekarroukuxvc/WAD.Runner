using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

public sealed class CkvdConfigurationRules : IModelConfigurationRules
{
    public ConfigurationPlan Resolve(
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeData? wedge,
        IReadOnlyList<FeatureToggleStep>? explicitToggleSteps = null)
    {
        string config = subclass switch
        {
            WedgeSubclass.PGB when drawingType == DrawingType.Overlay => "PGB_OVERLAY",
            WedgeSubclass.PGB when drawingType == DrawingType.Customer => "PGB_CUSTOMER_DRAWING",
            WedgeSubclass.PGB => "PGB_DRAWING",
            _ when drawingType == DrawingType.Overlay => "FG_OVERLAY",
            _ when drawingType == DrawingType.Customer => "FG_CUSTOMER_DRAWING",
            _ => "FG_PRODUCTION_DRAWING"
        };

        var plan = ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps)
            ? ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps)
            : ConfigurationPlanFactory.ForActive(config);

        Logger.Info($"[CkvdConfigurationRules] {subclass}/{drawingType} -> {plan.ConfigurationName}/{plan.ToggleMode}");
        return plan;
    }
}

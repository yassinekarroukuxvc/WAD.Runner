// ModelAutomation/Rules/CKVD/CkvdConfigurationRules.cs
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.CKVD
{
    /// <summary>
    /// Configuration rules for CKVD wedges.
    ///
    /// CKVD uses named configurations per subclass + drawing type combination.
    /// Feature toggles are normally applied to the active configuration only,
    /// because each CKVD configuration is self-contained.
    ///
    /// If explicit toggle steps are supplied, they take precedence and toggles
    /// are applied only to those exact configurations.
    /// </summary>
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

            if (ConfigurationPlanFactory.HasExplicitSteps(explicitToggleSteps))
            {
                Logger.Info($"[CkvdConfigRules] subclass={subclass}, drawingType={drawingType} → config={config} / ExplicitSteps");
                return ConfigurationPlanFactory.ForExplicit(config, explicitToggleSteps);
            }

            Logger.Info($"[CkvdConfigRules] subclass={subclass}, drawingType={drawingType} → config={config} / ActiveConfiguration");
            return ConfigurationPlanFactory.ForActive(config);
        }
    }
}

// ModelAutomation/Rules/CKVD/CkvdConfigurationRules.cs
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.CKVD
{
    /// <summary>
    /// Configuration rules for CKVD wedges.
    ///
    /// CKVD uses named configurations per subclass + drawing type combination.
    /// Feature toggles are applied to the active (this) configuration only,
    /// because each CKVD configuration is fully independent.
    /// </summary>
    public sealed class CkvdConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge)
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

            Logger.Info($"[CkvdConfigRules] subclass={subclass}, drawingType={drawingType} → config={config} / ThisConfiguration");

            // CKVD applies toggles to the active config only — each config is self-contained.
            return new ConfigurationPlan(config, swInConfigurationOpts_e.swThisConfiguration);
        }
    }
}

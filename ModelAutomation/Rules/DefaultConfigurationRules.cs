// ModelAutomation/Rules/DefaultConfigurationRules.cs
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// Safe fallback configuration rules for unimplemented or future wedge types.
    /// Returns "Default" and applies toggles across all configurations to avoid
    /// stale state in any config.
    /// </summary>
    public sealed class DefaultConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge)
        {
            Logger.Info("[DefaultConfigRules] Unknown wedge type → Default / AllConfigurations");
            return new ConfigurationPlan("Default", swInConfigurationOpts_e.swAllConfiguration);
        }
    }
}

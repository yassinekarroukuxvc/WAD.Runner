// ModelAutomation/Rules/UTUS/UtusConfigurationRules.cs
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.UTUS
{
    /// <summary>
    /// Configuration rules for UTUS wedges.
    /// UTUS uses the same configuration selection logic as COB.
    /// See <see cref="COB.CobConfigurationRules"/> for the full rationale.
    /// </summary>
    public sealed class UtusConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge)
        {
            if (drawingType != DrawingType.Overlay)
            {
                Logger.Info("[UtusConfigRules] Non-overlay → Default / ThisConfiguration");
                return new ConfigurationPlan("Default", swInConfigurationOpts_e.swThisConfiguration);
            }

            if (subclass == WedgeSubclass.PGB)
            {
                Logger.Info("[UtusConfigRules] Overlay + PGB → Default / AllConfigurations");
                return new ConfigurationPlan("Default", swInConfigurationOpts_e.swAllConfiguration);
            }

            bool hasVw = IsDimPositive(wedge, "VW");
            bool hasVr = IsDimPositive(wedge, "VR");

            string config;
            if (!hasVw && !hasVr) config = "std_cut";
            else if (hasVw && hasVr) config = "non_std_cut";
            else config = "Default";

            Logger.Info(
                $"[UtusConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / AllConfigurations");

            return new ConfigurationPlan(config, swInConfigurationOpts_e.swAllConfiguration);
        }

        private static bool IsDimPositive(WedgeData? wedge, string key)
        {
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null) return false;
            return dim.Nominal.Value > 0m;
        }
    }
}

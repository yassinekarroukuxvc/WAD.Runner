// ModelAutomation/Rules/FP/FpConfigurationRules.cs
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.FP
{
    /// <summary>
    /// Configuration rules for FP wedges.
    /// FP uses the same configuration selection logic as COB.
    /// See <see cref="COB.CobConfigurationRules"/> for the full rationale.
    /// </summary>
    public sealed class FpConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge)
        {
            if (drawingType != DrawingType.Overlay)
            {
                Logger.Info("[FpConfigRules] Non-overlay → Default / ThisConfiguration");
                return new ConfigurationPlan("Default", swInConfigurationOpts_e.swThisConfiguration);
            }

            if (subclass == WedgeSubclass.PGB)
            {
                Logger.Info("[FpConfigRules] Overlay + PGB → Default / AllConfigurations");
                return new ConfigurationPlan("Default", swInConfigurationOpts_e.swAllConfiguration);
            }

            bool hasVw = IsDimPositive(wedge, "VW");
            bool hasVr = IsDimPositive(wedge, "VR");

            string config;
            if (!hasVw && !hasVr) config = "std_cut";
            else if (hasVw && hasVr) config = "non_std_cut";
            else config = "Default";

            Logger.Info(
                $"[FpConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / AllConfigurations");

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

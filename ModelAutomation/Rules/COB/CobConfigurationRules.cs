// ModelAutomation/Rules/COB/CobConfigurationRules.cs
using SolidWorks.Interop.swconst;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.COB
{
    /// <summary>
    /// Configuration rules for COB wedges.
    ///
    /// Non-overlay (Production / Customer):
    ///   - Always "Default", toggles applied to THIS configuration only.
    ///
    /// Overlay + PGB:
    ///   - Always "Default", toggles applied to ALL configurations
    ///     (PGB overlay template has no per-config variants).
    ///
    /// Overlay + FG:
    ///   - No VW and no VR  → "std_cut",     ALL configurations
    ///   - VW > 0 and VR > 0 → "non_std_cut", ALL configurations
    ///   - Otherwise         → "Default",     ALL configurations
    ///
    /// Rationale for ALL on overlay:
    ///   The COB overlay template is controlled by feature suppression across
    ///   multiple configurations; applying toggles to only the active config
    ///   leaves the other configs in a stale state.
    /// </summary>
    public sealed class CobConfigurationRules : IModelConfigurationRules
    {
        public ConfigurationPlan Resolve(WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge)
        {
            // Non-overlay: simple, active config only
            if (drawingType != DrawingType.Overlay)
            {
                Logger.Info("[CobConfigRules] Non-overlay → Default / ThisConfiguration");
                return new ConfigurationPlan("Default", swInConfigurationOpts_e.swThisConfiguration);
            }

            // Overlay + PGB
            if (subclass == WedgeSubclass.PGB)
            {
                Logger.Info("[CobConfigRules] Overlay + PGB → Default / AllConfigurations");
                return new ConfigurationPlan("Default", swInConfigurationOpts_e.swAllConfiguration);
            }

            // Overlay + FG: configuration driven by VW / VR presence
            bool hasVw = IsDimPositive(wedge, "VW");
            bool hasVr = IsDimPositive(wedge, "VR");

            string config;
            if (!hasVw && !hasVr) config = "std_cut";
            else if (hasVw && hasVr) config = "non_std_cut";
            else config = "Default";

            Logger.Info(
                $"[CobConfigRules] Overlay + FG → hasVW={hasVw}, hasVR={hasVr} → config={config} / AllConfigurations");

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

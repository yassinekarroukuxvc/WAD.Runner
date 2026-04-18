// ModelAutomation/Rules/IModelConfigurationRules.cs
using SolidWorks.Interop.swconst;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// The result of configuration planning for a single job.
    /// Tells the orchestrator:
    ///   - which SW configuration to activate
    ///   - which scope to use when applying feature toggles
    /// </summary>
    public sealed record ConfigurationPlan(
        string ConfigurationName,
        swInConfigurationOpts_e ToggleScope
    );

    /// <summary>
    /// Per-wedge-type rule set that decides which SolidWorks configuration to activate
    /// and which scope to apply feature toggles in.
    ///
    /// One implementation per wedge type. The orchestrator never contains
    /// configuration-selection logic — it only calls this interface.
    ///
    /// Design contract (pure logic, no SW calls):
    ///   - Implementations must never throw for valid input.
    ///   - If the combination is unrecognised, return "Default" + swAllConfiguration
    ///     as the safe fallback.
    /// </summary>
    public interface IModelConfigurationRules
    {
        ConfigurationPlan Resolve(WedgeSubclass subclass, DrawingType drawingType, WedgeData? wedge);
    }
}

// ModelAutomation/Rules/ConfigurationRulesFactory.cs
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.UTUS;

namespace WAD.Runner.ModelAutomation.Rules
{
    /// <summary>
    /// Maps a WedgeType to its <see cref="IModelConfigurationRules"/> implementation.
    /// This is the ONLY place in the codebase that knows which class handles which wedge type.
    ///
    /// To add a new wedge type:
    ///   1. Create Rules/{NewType}/{NewType}ConfigurationRules.cs
    ///   2. Add one line here.
    ///   That's it. The orchestrator never changes.
    /// </summary>
    public static class ConfigurationRulesFactory
    {
        public static IModelConfigurationRules For(WedgeType wedgeType)
            => wedgeType switch
            {
                WedgeType.CKVD => new CkvdConfigurationRules(),
                WedgeType.COB => new CobConfigurationRules(),
                WedgeType.UTUS => new UtusConfigurationRules(),
                WedgeType.FP => new FpConfigurationRules(),
                _ => new DefaultConfigurationRules()
            };
    }
}

using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// UTUS uses the shared COB-like configuration selection logic.
/// </summary>
public sealed class UtusConfigurationRules : CobLikeConfigurationRulesBase
{
    public UtusConfigurationRules() : base("UtusConfigRules")
    {
    }
}

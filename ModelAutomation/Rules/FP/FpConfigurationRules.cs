using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.FP;

/// <summary>
/// FP uses the shared COB-like configuration selection logic.
/// </summary>
public sealed class FpConfigurationRules : CobLikeConfigurationRulesBase
{
    public FpConfigurationRules() : base("FpConfigRules")
    {
    }
}

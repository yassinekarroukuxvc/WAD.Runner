using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.FP;

/// <summary>
/// FP uses the shared COB-like overlay tolerance planning logic.
/// </summary>
public sealed class FpToleranceRules : CobLikeToleranceRulesBase
{
    public FpToleranceRules() : base("FpToleranceRules")
    {
    }
}

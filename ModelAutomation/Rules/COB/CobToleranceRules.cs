using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.COB;

/// <summary>
/// COB uses the shared COB-like overlay tolerance planning logic.
/// </summary>
public sealed class CobToleranceRules : CobLikeToleranceRulesBase
{
    public CobToleranceRules() : base("CobToleranceRules")
    {
    }
}

using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// UTUS uses the shared COB-like overlay tolerance planning logic.
/// </summary>
public sealed class UtusToleranceRules : CobLikeToleranceRulesBase
{
    public UtusToleranceRules() : base("UtusToleranceRules")
    {
    }
}

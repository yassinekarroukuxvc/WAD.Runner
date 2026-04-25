using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.FP;

/// <summary>
/// FP keeps the shared COB-like logic,
/// then forces VW and SLB on for the active shank.
/// </summary>
public sealed class FpFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => "FpFeatureRules";

    protected override void ApplyVariantAdjustments(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context,
        HashSet<string> active,
        HashSet<string> forceSuppress)
    {
        AddFeatureGroup(active, "ROUND_BR", shank);
    }
}

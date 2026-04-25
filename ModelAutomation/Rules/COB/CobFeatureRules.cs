using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.COB;

/// <summary>
/// COB uses the shared COB-like rules, but always keeps ROUND_BR
/// unsuppressed for the active shank.
/// </summary>
public sealed class CobFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => "CobFeatureRules";

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
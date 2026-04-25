using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// UT/US keeps the shared COB-like logic,
/// but ROUND_BR stays suppressed for both shanks.
/// </summary>
public sealed class UtusFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => "UtusFeatureRules";

    protected override void ApplyVariantAdjustments(
        CobLikeRuleFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context,
        HashSet<string> active,
        HashSet<string> forceSuppress)
    {
        AddFeatureGroup(forceSuppress, "ROUND_BR", CobLikeShankType.Std);
        AddFeatureGroup(forceSuppress, "ROUND_BR", CobLikeShankType.Rev180);

        Logger.Info($"[{LogPrefix}] UT/US rule -> ROUND_BR forced suppressed for STD and 180_DEG_REV.");
    }
}

using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CobLike;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

public sealed class UtusFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => nameof(UtusFeatureRules);

    protected override void ApplyVariantAdjustments(CobLikeFacts facts, CobLikeShankType shank, FeatureRuleContext context, FeaturePlanBuilder plan)
    {
        SuppressFeature(plan, "ROUND_BR", CobLikeShankType.Std);
        SuppressFeature(plan, "ROUND_BR", CobLikeShankType.Rev180);
        Logger.Info($"[{LogPrefix}] ROUND_BR forced suppressed for STD and 180_DEG_REV.");
    }
}
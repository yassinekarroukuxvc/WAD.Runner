using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CobLike;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.COB;

public sealed class CobFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => nameof(CobFeatureRules);

    protected override void ApplyVariantAdjustments(CobLikeFacts facts, CobLikeShankType shank, FeatureRuleContext context, FeaturePlanBuilder plan)
    {
        ActivateFeature(plan, "ROUND_BR", shank);
    }
}

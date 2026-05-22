using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.CobLike;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.FP;

public sealed class FpFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => nameof(FpFeatureRules);

    protected override void ApplyVariantAdjustments(CobLikeFacts facts, CobLikeShankType shank, FeatureRuleContext context, FeaturePlanBuilder plan)
    {
        AddFeatureGroup(plan, "ROUND_BR", shank);
    }
}

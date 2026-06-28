using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules;

public sealed class DefaultFeatureRules : IFeatureRuleSet
{
    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
        => ModelRuleRunner.FeaturePlan.Empty;
}

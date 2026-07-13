using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules.CobLike;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.COB;

public sealed class CobToleranceRules : CobLikeToleranceRulesBase
{
    public CobToleranceRules() : base(nameof(CobToleranceRules)) { }

    protected override void ApplySubclassSpecificTolerances(
        List<ToleranceUpdate> updates,
        CobLikeRuleFacts facts,
        WedgeSubclass subclass)
    {
        var footOption = facts.ResolveFootOption();

        // Resolve the foot-width sketch (smallest of W / VW / W2 for this foot option).
        var footSketch = ResolveFootWidthSketch(facts);

        switch (footOption)
        {
            case CobLikeFootOption.C:
            case CobLikeFootOption.C_WithCbr:
                AddComputedBoundsMm(updates, facts, "CL", $"CL_MAX@{footSketch}", $"CL_MIN@{footSketch}");
                AddComputedBoundsMm(updates, facts, "CD", $"CD_MAX@{footSketch}", $"CD_MIN@{footSketch}");
                break;

            case CobLikeFootOption.G:
                AddComputedBoundsMm(updates, facts, "GD", $"GD_MAX@{footSketch}", $"GD_MIN@{footSketch}");
                AddComputedBoundsMm(updates, facts, "GR", $"GR_MAX@{footSketch}", $"GR_MIN@{footSketch}");
                break;

            case CobLikeFootOption.VG:
                AddComputedBoundsMm(updates, facts, "B", $"B_MAX@{footSketch}", $"B_MIN@{footSketch}");
                AddComputedBoundsMm(updates, facts, "GA", $"GA_MAX@{footSketch}", $"GA_MIN@{footSketch}");
                AddComputedBoundsMm(updates, facts, "GD", $"GD_MAX@{footSketch}", $"GD_MIN@{footSketch}");
                break;
        }

        Logger.Info($"[{nameof(CobToleranceRules)}] Foot={footOption}, resolved sketch={footSketch}");
    }
}

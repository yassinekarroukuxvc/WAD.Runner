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

        switch (footOption)
        {
            case CobLikeFootOption.C:
            case CobLikeFootOption.C_WithCbr:
                AddComputedBoundsMm(updates, facts, "CL", "CL_MAX@C_FOOT_overlay_sketch", "CL_MIN@C_FOOT_overlay_sketch");
                AddComputedBoundsMm(updates, facts, "CD", "CD_MAX@C_FOOT_overlay_sketch", "CD_MIN@C_FOOT_overlay_sketch");
                break;

            case CobLikeFootOption.G:
                AddComputedBoundsMm(updates, facts, "GD", "GD_MAX@G_FOOT_overlay_sketch", "GD_MIN@G_FOOT_overlay_sketch");
                AddComputedBoundsMm(updates, facts, "GO", "GO_MAX@G_FOOT_overlay_sketch", "GO_MIN@G_FOOT_overlay_sketch");
                break;

            case CobLikeFootOption.VG:
                AddComputedBoundsMm(updates, facts, "B", "B_MAX@VG_FOOT_overlay_sketch", "B_MIN@VG_FOOT_overlay_sketch");
                AddComputedBoundsMm(updates, facts, "GA", "GA_MAX@VG_FOOT_overlay_sketch", "GA_MIN@VG_FOOT_overlay_sketch");
                AddComputedBoundsMm(updates, facts, "GD", "GD_MAX@VG_FOOT_overlay_sketch", "GD_MIN@VG_FOOT_overlay_sketch");
                break;
        }
    }
}
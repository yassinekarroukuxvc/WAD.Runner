using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.FP;

/// <summary>
/// FP uses the shared COB-like feature planning,
/// plus one FP-specific mandatory activation layer:
/// the active shank always keeps VW and SLB enabled.
/// </summary>
public sealed class FpFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => "FpFeatureRules";
    protected override string Pgb180RevConfigurationHint => "FP_180_DEG_REV_PGB";

    protected override void ApplyVariantAdjustments(
        CobLikeShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
    {
        var opposite = shank == CobLikeShankType.Std ? CobLikeShankType.Rev180 : CobLikeShankType.Std;

        foreach (var nm in BuildNameCandidatesWithSketches("VW", shank))
        {
            unsuppress.Add(nm);
            suppress.Remove(nm);
        }

        foreach (var nm in BuildNameCandidatesWithSketches("SLB", shank))
        {
            unsuppress.Add(nm);
            suppress.Remove(nm);
        }

        foreach (var nm in BuildNameCandidatesWithSketches("VW", opposite))
        {
            suppress.Add(nm);
            unsuppress.Remove(nm);
        }

        foreach (var nm in BuildNameCandidatesWithSketches("SLB", opposite))
        {
            suppress.Add(nm);
            unsuppress.Remove(nm);
        }

        Logger.Info($"[{LogPrefix}] FP mandatory rule applied for shank={shank}: always enable full VW + SLB set.");
    }
}

using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Rules.CobLike;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// UTUS uses the shared COB-like feature planning,
/// plus the UTUS-specific rule that ROUND_BR stays suppressed
/// for both shanks at all times.
/// </summary>
public sealed class UtusFeatureRules : CobLikeFeatureRulesBase
{
    protected override string LogPrefix => "UtusFeatureRules";
    protected override string Pgb180RevConfigurationHint => "UTUS_180_DEG_REV_PGB";

    protected override void ApplyVariantAdjustments(
        CobLikeShankType shank,
        HashSet<string> suppress,
        HashSet<string> unsuppress)
    {
        foreach (var nm in BuildNameCandidatesWithSketches("ROUND_BR", CobLikeShankType.Std))
            suppress.Add(nm);

        foreach (var nm in BuildNameCandidatesWithSketches("ROUND_BR", CobLikeShankType.Rev180))
            suppress.Add(nm);

        unsuppress.RemoveWhere(nm =>
            nm.StartsWith("ROUND_BR_", StringComparison.OrdinalIgnoreCase));

        Logger.Info($"[{LogPrefix}] Special rule applied: ROUND_BR forced suppressed for STD and 180_DEG_REV.");
    }
}

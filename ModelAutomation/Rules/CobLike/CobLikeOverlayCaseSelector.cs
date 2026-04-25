using System;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

internal enum CobLikeOverlayVwCase
{
    None = 0,
    Case1 = 1,
    Case2 = 2,
    Case3 = 3,
    Case4 = 4
}

/// <summary>
/// Resolves which VW-left overlay sketch case should be used for COB-like wedges.
///
/// Existing behavior:
/// - Case 1 when VW != W
/// - Case 2 when VW == W
///
/// New large-VR behavior:
/// - Case 4 when VR is too large AND VW == W
/// - Case 3 otherwise when VR is too large
///
/// "VR is too large" is intentionally aligned with the overlay non_std_cut soft-cap logic,
/// so the sketch switch happens at the same point where overlay non_std_cut would begin
/// to be compressed for drawing stability.
/// </summary>
internal static class CobLikeOverlayCaseSelector
{
    public const decimal OverlayTlMm = 30.0m;
    public const decimal OverlayLargeVrSoftCapMm = OverlayTlMm * 0.012m; // 0.36 mm
    private const decimal ExtraClearanceFactor = 0.20m;

    public static CobLikeOverlayVwCase Resolve(CobLikeRuleFacts facts, bool useUtusFormula)
    {
        if (facts is null) throw new ArgumentNullException(nameof(facts));

        if (!facts.HasVw)
            return CobLikeOverlayVwCase.None;

        bool vwEqualsW = facts.AreNominalsEqual("VW", "W");
        bool largeVr = IsVrTooLargeForOverlay(facts, useUtusFormula);

        if (largeVr)
            return vwEqualsW ? CobLikeOverlayVwCase.Case4 : CobLikeOverlayVwCase.Case3;

        return vwEqualsW ? CobLikeOverlayVwCase.Case2 : CobLikeOverlayVwCase.Case1;
    }

    public static bool IsVrTooLargeForOverlay(CobLikeRuleFacts facts, bool useUtusFormula)
    {
        if (facts is null) throw new ArgumentNullException(nameof(facts));

        decimal rawMm = useUtusFormula
            ? ComputeUtusRawNonStdCutMm(facts)
            : ComputeCobLikeRawNonStdCutMm(facts);

        return rawMm > OverlayLargeVrSoftCapMm;
    }

    private static decimal ComputeUtusRawNonStdCutMm(CobLikeRuleFacts facts)
        => facts.TryGetNominalMm("VR", out var vrMm) && vrMm > 0m
            ? vrMm
            : 0m;

    private static decimal ComputeCobLikeRawNonStdCutMm(CobLikeRuleFacts facts)
    {
        decimal vrMax = TryGetMaxLikeMm(facts, explicitMaxKey: "VR_MAX", baseKey: "VR", out var resolvedVrMax)
            ? resolvedVrMax
            : 0m;

        decimal vrrMax = TryGetMaxLikeMm(facts, explicitMaxKey: "VRR_MAX", baseKey: "VRR", out var resolvedVrrMax)
            ? resolvedVrrMax
            : 0m;

        decimal clearance = vrMax * ExtraClearanceFactor;
        return vrMax + vrrMax + clearance;
    }

    private static bool TryGetMaxLikeMm(
        CobLikeRuleFacts facts,
        string explicitMaxKey,
        string baseKey,
        out decimal value)
    {
        value = 0m;

        if (facts.TryGetNominalMm(explicitMaxKey, out var explicitMaxMm))
        {
            value = explicitMaxMm;
            return true;
        }

        if (!facts.TryGetLengthNominalMm(baseKey, out var nominalMm))
            return false;

        if (!facts.TryGetLengthToleranceMm(baseKey, out _, out var upperTolMm))
            return false;

        value = nominalMm + upperTolMm;
        return true;
    }
}

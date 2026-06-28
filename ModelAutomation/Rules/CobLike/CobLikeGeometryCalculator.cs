using System;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

public static class CobLikeGeometryCalculator
{
    private const decimal DefaultFunnelGapInch = 0.0003m;
    private const decimal InchToMm = 25.4m;
    public const decimal DefaultFunnelGapMm = DefaultFunnelGapInch * InchToMm;

    private const double Epsilon = 1e-12;

    public static decimal ComputeFunnelGapMmOrDefault(CobLikeRuleFacts facts)
    {
        if (facts is null)
            throw new ArgumentNullException(nameof(facts));

        if (!facts.TryGetNominalMm("FNO", out var fnoMm) || fnoMm <= 0m)
            return DefaultFunnelGapMm;

        if (!facts.TryGetNominalDeg("FNA", out var fnaDeg)) return DefaultFunnelGapMm;
        if (!facts.TryGetNominalDeg("BA", out var baDeg)) return DefaultFunnelGapMm;
        if (!facts.TryGetNominalDeg("RA", out var raDeg)) return DefaultFunnelGapMm;
        if (!facts.TryGetNominalMm("H", out var hMm)) return DefaultFunnelGapMm;

        return ComputeFunnelGapMmOrDefault(fnoMm, fnaDeg, baDeg, raDeg, hMm);
    }

    public static decimal ComputeFunnelGapMmOrDefault(
        decimal fnoMm,
        decimal fnaDeg,
        decimal baDeg,
        decimal raDeg,
        decimal hMm)
    {
        var alphaRad = DegToRad((double)(fnaDeg / 2m));
        var kRad = DegToRad((double)(baDeg + raDeg));

        var sinAlpha = Math.Sin(alphaRad);
        if (Math.Abs(sinAlpha) <= Epsilon)
            return DefaultFunnelGapMm;

        var tanAlpha = Math.Tan(alphaRad);
        var tanK = Math.Tan(kRad);
        var sqrtInput = 1.0 - ((tanAlpha * tanAlpha) * (tanK * tanK));

        if (sqrtInput < 0.0)
            return DefaultFunnelGapMm;

        var denominator = 1.0 + (tanK * tanAlpha);
        if (Math.Abs(denominator) <= Epsilon)
            return DefaultFunnelGapMm;

        var fnoFactor = (double)fnoMm * (Math.Sqrt(sqrtInput) / denominator);
        var funnelGap = (fnoFactor - (double)hMm) / (2.0 * sinAlpha);

        if (double.IsNaN(funnelGap) || double.IsInfinity(funnelGap) || funnelGap <= 0.0)
            return DefaultFunnelGapMm;

        return (decimal)funnelGap;
    }

    public static decimal ComputeNonStdCutRawMm(CobLikeRuleFacts facts)
    {
        if (facts is null)
            throw new ArgumentNullException(nameof(facts));

        var vrMax = facts.TryGetMaxLikeMm("VR_MAX", "VR", out var resolvedVrMax)
            ? resolvedVrMax
            : 0m;

        var vrrMax = facts.TryGetMaxLikeMm("VRR_MAX", "VRR", out var resolvedVrrMax)
            ? resolvedVrrMax
            : 0m;

        return vrMax + vrrMax;
    }

    private static double DegToRad(double degrees) => degrees * (Math.PI / 180.0);
}

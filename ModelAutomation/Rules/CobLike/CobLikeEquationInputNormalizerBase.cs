using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared equation normalization for COB / FP / UTUS.
/// This keeps derived dimensions and defaulting logic in one authoritative place.
/// </summary>
public abstract class CobLikeEquationInputNormalizerBase : IEquationInputNormalizer
{
    private const decimal DefaultFunnelGapInch = 0.0003m;
    private const decimal InchToMm = 25.4m;
    private static readonly decimal DefaultFunnelGapMm = DefaultFunnelGapInch * InchToMm;
    private const decimal DefaultVraDeg = 90m;
    private const double Eps = 1e-12;

    private readonly string _logPrefix;

    protected CobLikeEquationInputNormalizerBase(string logPrefix)
    {
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "CobLikeEquationInputNormalizer" : logPrefix;
    }

    public IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var facts = new CobLikeRuleFacts(wedge);
        var result = new Dictionary<DimensionKey, Dimension>(wedge.Dimensions);

        ApplyVraDefaultRule(facts, result);

        var funnelGapMm = ComputeFunnelGapMmOrDefault(facts);
        UpsertLengthMm(result, "funnel_gap", funnelGapMm);

        Logger.Info($"[{_logPrefix}] funnel_gap = {funnelGapMm} mm");
        return result;
    }

    private void ApplyVraDefaultRule(CobLikeRuleFacts facts, IDictionary<DimensionKey, Dimension> result)
    {
        bool anyVPresent =
            (facts.TryGetNominalValue("VW", out var vw) && vw > 0m) ||
            (facts.TryGetNominalValue("VRR", out var vrr) && vrr > 0m) ||
            (facts.TryGetNominalValue("VR", out var vr) && vr > 0m);

        if (!anyVPresent)
            return;

        var vraKey = DimensionKey.From("VRA");
        bool hasVra = facts.Wedge.Dimensions.TryGetValue(vraKey, out var vraDim) && vraDim is not null;
        bool vraIsZero = hasVra && vraDim!.Nominal.Value == 0m;

        if (hasVra && !vraIsZero)
            return;

        UpsertAngleDeg(result, "VRA", DefaultVraDeg);
        Logger.Info($"[{_logPrefix}] Rule applied: (VW/VRR/VR > 0) and (VRA missing/0) → VRA = 90deg");
    }

    private static decimal ComputeFunnelGapMmOrDefault(CobLikeRuleFacts facts)
    {
        if (!facts.TryGetNominalMm("FNO", out var fnoMm) || fnoMm <= 0m)
            return DefaultFunnelGapMm;

        if (!facts.TryGetNominalDeg("FNA", out var fnaDeg)) return DefaultFunnelGapMm;
        if (!facts.TryGetNominalDeg("BA", out var baDeg)) return DefaultFunnelGapMm;
        if (!facts.TryGetNominalDeg("RA", out var raDeg)) return DefaultFunnelGapMm;
        if (!facts.TryGetNominalMm("H", out var hMm)) return DefaultFunnelGapMm;

        var alphaDeg = fnaDeg / 2m;
        var kDeg = baDeg + raDeg;

        var alphaRad = DegToRad((double)alphaDeg);
        var kRad = DegToRad((double)kDeg);

        var sinAlpha = Math.Sin(alphaRad);
        if (Math.Abs(sinAlpha) <= Eps)
            return DefaultFunnelGapMm;

        var tanA = Math.Tan(alphaRad);
        var tanK = Math.Tan(kRad);

        var tanA2 = tanA * tanA;
        var tanK2 = tanK * tanK;

        var denom = 1.0 + (tanA * tanK);
        if (Math.Abs(denom) <= Eps)
            return DefaultFunnelGapMm;

        var frac = (1.0 - (tanA2 * tanK2)) / denom;
        var bracketMm = ((double)fnoMm * frac) - (double)hMm;
        var fg = (1.0 / (2.0 * sinAlpha)) * bracketMm;

        if (double.IsNaN(fg) || double.IsInfinity(fg) || fg <= 0.0)
            return DefaultFunnelGapMm;

        return (decimal)fg;
    }

    private static void UpsertLengthMm(IDictionary<DimensionKey, Dimension> dims, string key, decimal mm)
    {
        var dk = DimensionKey.From(key);
        dims[dk] = Dimension.CreateLength(
            key: dk,
            nominalMm: Quantity.MmOf(mm),
            tolMm: Tolerance.Zero,
            comment: null);
    }

    private static void UpsertAngleDeg(IDictionary<DimensionKey, Dimension> dims, string key, decimal deg)
    {
        var dk = DimensionKey.From(key);
        dims[dk] = Dimension.CreateAngle(
            key: dk,
            nominalDeg: Quantity.DegOf(deg),
            comment: null);
    }

    private static double DegToRad(double deg) => deg * (Math.PI / 180.0);
}

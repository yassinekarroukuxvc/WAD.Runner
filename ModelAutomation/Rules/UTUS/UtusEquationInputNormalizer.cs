// ModelAutomation/Rules/Equations/UtusEquationInputNormalizer.cs
using System;
using System.Collections.Generic;

using WAD.Runner.Application; // Logger
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules.UTUS
{
    /// <summary>
    /// UTUS-specific dimension normalization (pure logic).
    /// Adds derived dimensions that are not provided by the DB (e.g., funnel_gap),
    /// and applies UTUS-specific defaulting rules for missing inputs.
    ///
    /// Current behavior intentionally matches COB normalization.
    /// </summary>
    public sealed class UtusEquationInputNormalizer : IEquationInputNormalizer
    {
        // "0.0003 inch" default (per note) -> mm
        private const decimal DefaultFunnelGapInch = 0.0003m;
        private const decimal InchToMm = 25.4m;
        private static readonly decimal DefaultFunnelGapMm = DefaultFunnelGapInch * InchToMm; // 0.00762 mm

        // Default VRA when (VW or VRR or VR) > 0 and VRA missing/0
        private const decimal DefaultVraDeg = 90m;

        private const double Eps = 1e-12;

        public IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            // Clone into mutable (never mutate wedge.Dimensions)
            var result = new Dictionary<DimensionKey, Dimension>(wedge.Dimensions);

            // Rule:
            // If any of VW / VRR / VR > 0 AND VRA is missing or == 0 => set VRA = 90deg
            ApplyVraDefaultRule(wedge, result);

            // funnel_gap formula inputs (all in deg + mm)
            // alpha = FNA/2 (deg)
            // k = BA + RA (deg)
            // funnel_gap = 1/(2*sin(alpha)) * [ FND * ((1 - tan^2(alpha)*tan^2(k)) / (1 + tan^2(alpha)*tan^2(k))) - H ]
            //
            // If FND missing/zero -> funnel_gap = 0.0003" = 0.00762 mm
            var funnelGapMm = ComputeFunnelGapMmOrDefault(wedge);

            UpsertLengthMm(result, "funnel_gap", funnelGapMm);

            Logger.Info($"[UtusEquationInputNormalizer] funnel_gap = {funnelGapMm} mm");

            return result;
        }

        private static void ApplyVraDefaultRule(WedgeData wedge, IDictionary<DimensionKey, Dimension> result)
        {
            // Condition A: any of (VW, VRR, VR) is present and > 0 (numeric check only)
            bool anyVPresent =
                (TryGetNominalValue(wedge, "VW", out var vw) && vw > 0m) ||
                (TryGetNominalValue(wedge, "VRR", out var vrr) && vrr > 0m) ||
                (TryGetNominalValue(wedge, "VR", out var vr) && vr > 0m);

            if (!anyVPresent)
                return;

            // Condition B: VRA missing or equals to 0
            var vraKey = DimensionKey.From("VRA");

            bool hasVra = wedge.Dimensions.TryGetValue(vraKey, out var vraDim) && vraDim is not null;
            bool vraIsZero = hasVra && vraDim!.Nominal.Value == 0m;

            if (hasVra && !vraIsZero)
                return;

            // Insert/overwrite VRA as 90deg
            UpsertAngleDeg(result, "VRA", DefaultVraDeg);

            Logger.Info("[UtusEquationInputNormalizer] Rule applied: (VW/VRR/VR > 0) and (VRA missing/0) → VRA = 90deg");
        }

        private static decimal ComputeFunnelGapMmOrDefault(WedgeData wedge)
        {
            // If FND is missing or zero => default
            if (!TryGetNominalMm(wedge, "FND", out var fndMm) || fndMm <= 0m)
                return DefaultFunnelGapMm;

            // Need the other inputs; if any missing => default (safer than partial math)
            if (!TryGetNominalDeg(wedge, "FNA", out var fnaDeg)) return DefaultFunnelGapMm;
            if (!TryGetNominalDeg(wedge, "BA", out var baDeg)) return DefaultFunnelGapMm;
            if (!TryGetNominalDeg(wedge, "RA", out var raDeg)) return DefaultFunnelGapMm;
            if (!TryGetNominalMm(wedge, "H", out var hMm)) return DefaultFunnelGapMm;

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

            var denom = 1.0 + (tanA2 * tanK2);
            if (Math.Abs(denom) <= Eps)
                return DefaultFunnelGapMm;

            var frac = (1.0 - (tanA2 * tanK2)) / denom;

            // bracket = FND * frac - H
            var bracketMm = ((double)fndMm * frac) - (double)hMm;

            // funnel_gap = 1/(2*sin(alpha)) * bracket
            var fg = (1.0 / (2.0 * sinAlpha)) * bracketMm;

            // If math produces nonsense (negative/NaN/inf), fall back
            if (double.IsNaN(fg) || double.IsInfinity(fg) || fg <= 0.0)
                return DefaultFunnelGapMm;

            return (decimal)fg;
        }

        private static void UpsertLengthMm(IDictionary<DimensionKey, Dimension> dims, string key, decimal mm)
        {
            var dk = DimensionKey.From(key);
            var dim = Dimension.CreateLength(
                key: dk,
                nominalMm: Quantity.MmOf(mm),
                tolMm: Tolerance.Zero,
                comment: null);

            dims[dk] = dim;
        }

        private static void UpsertAngleDeg(IDictionary<DimensionKey, Dimension> dims, string key, decimal deg)
        {
            var dk = DimensionKey.From(key);
            var dim = Dimension.CreateAngle(
                key: dk,
                nominalDeg: Quantity.DegOf(deg),
                comment: null);

            dims[dk] = dim;
        }

        private static bool TryGetNominalMm(WedgeData wedge, string key, out decimal mm)
        {
            mm = 0m;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim))
                return false;

            if (!dim.Nominal.IsMm)
                return false;

            mm = dim.Nominal.AsMm();
            return true;
        }

        private static bool TryGetNominalDeg(WedgeData wedge, string key, out decimal deg)
        {
            deg = 0m;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim))
                return false;

            if (!dim.Nominal.IsDeg)
                return false;

            deg = dim.Nominal.AsDeg();
            return true;
        }

        // Numeric presence check that doesn't care about unit kind; this rule only needs "> 0"
        private static bool TryGetNominalValue(WedgeData wedge, string key, out decimal value)
        {
            value = 0m;

            if (wedge?.Dimensions is null)
                return false;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null)
                return false;

            value = dim.Nominal.Value;
            return true;
        }

        private static double DegToRad(double deg) => deg * (Math.PI / 180.0);
    }
}
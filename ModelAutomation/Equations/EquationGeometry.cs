using SQLitePCL;
using System;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Equations;

internal static class EquationGeometry
{
    private const decimal DefaultFunnelGapInch = 0.0003m;
    private const decimal InchToMm = 25.4m;
    public const decimal DefaultFunnelGapMm = DefaultFunnelGapInch * InchToMm;
    public const decimal LargeOverlayVrThresholdMm = 0.5m;

    public static decimal FunnelGapMmOrDefault(WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm("FNO", out var fno) || fno <= 0m) return DefaultFunnelGapMm;
        if (!facts.TryGetAngleDeg("FNA", out var fna)) return DefaultFunnelGapMm;
        if (!facts.TryGetAngleDeg("RA", out var ra)) return DefaultFunnelGapMm;
        if (!facts.TryGetLengthMm("H", out var h)) return DefaultFunnelGapMm;

        decimal ba;

        if (facts.TryGetLengthMm("VBL", out var slb) && slb > 0m)
        {
            ba = 0m;
        }
        else
        {
            if (!facts.TryGetAngleDeg("BA", out ba)) return DefaultFunnelGapMm;
        }

        var alpha = DegToRad((double)(fna / 2m));
        var k = DegToRad((double)(ba + ra));

        var sinAlpha = Math.Sin(alpha);
        if (Math.Abs(sinAlpha) <= 1e-12) return DefaultFunnelGapMm;

        var tanA = Math.Tan(alpha);
        var tanK = Math.Tan(k);

        var sqrtInput = 1.0 - ((tanA * tanA) * (tanK * tanK));
        if (sqrtInput < 0.0) return DefaultFunnelGapMm;

        var denominator = 1.0 + (tanK * tanA);
        if (Math.Abs(denominator) <= 1e-12) return DefaultFunnelGapMm;

        var fnoFactor = (double)fno * (Math.Sqrt(sqrtInput) / denominator);
        var gap = (fnoFactor - (double)h) / (2.0 * sinAlpha);

        if (double.IsNaN(gap) || double.IsInfinity(gap) || gap <= 0.0)
            return DefaultFunnelGapMm;

        return (decimal)gap;
    }

    public static decimal NonStdCutRawMm(WedgeFacts facts)
    {
        var vr = facts.TryGetMaxLikeMm("VR_MAX", "VR", out var vrMax) ? vrMax : 0m;
        var vrr = facts.TryGetMaxLikeMm("VRR_MAX", "VRR", out var vrrMax) ? vrrMax : 0m;
        Logger.Success($"[EquationGeometry] VR = {vr}mm VRR={vrr}mm VR+VRR={vr+vrr}mm.");
        return vr + vrr;
    }

    public static double OverlayMagnification(WedgeFacts facts, WedgeType wedgeType)
    {
        var source = wedgeType is WedgeType.CKVD or WedgeType.OSG7 ? "FL" : "T";
        if (!facts.TryGetLengthMm(source, out var value) || value <= 0m)
        {
            Logger.Warn($"[EquationGeometry] Overlay magnification source '{source}' missing/invalid for {wedgeType}. Using 100.");
            return 100.0;
        }

        var mm = (double)value;
        if (mm <= 0.3403) return 400;
        if (mm <= 0.4572) return 300;
        if (mm <= 0.6908) return 200;
        return 100;
    }

    public static double OverlayScaleDecimal(double magnification)
        => (int)Math.Round(magnification) switch
        {
            400 => 246.0,
            300 => 183.0,
            200 => 122.7,
            _ => 60.8
        };

    public static decimal OverlaySafeNonStdCutMm(decimal rawMm, double scaleDecimal, WedgeType wedgeType)
    {
        if (rawMm <= 0m) return 0m;
        if (rawMm <= LargeOverlayVrThresholdMm) return rawMm;

        const decimal referenceInch = 2.01175m;
        const decimal inchToMm = 25.4m;
        var resolvedScale = scaleDecimal > 0.0 ? (decimal)scaleDecimal : 60.8m;
        var finalMm = referenceInch * inchToMm / resolvedScale;
        Logger.Warn($"[EquationGeometry] {wedgeType} overlay non_std_cut override: raw={rawMm}mm -> {finalMm}mm scale={resolvedScale}");
        return finalMm;
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
}

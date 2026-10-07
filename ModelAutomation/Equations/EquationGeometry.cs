using SQLitePCL;
using System;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Equations;

internal static class EquationGeometry
{
    private const decimal DefaultFunnelGapInch = 0.0003m;
    private const decimal OverlayReferenceCutInch = 1.5m;
    private const decimal InchToMm = 25.4m;
    private const decimal DefaultOverlayScaleDecimal = 60.8m;

    public const decimal DefaultFunnelGapMm = DefaultFunnelGapInch * InchToMm;
    public const decimal LargeOverlayVrThresholdMm = 0.5m;

    public static decimal FunnelGapMmOrDefault(WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm("FNO", out var fno) || fno <= 0m) return DefaultFunnelGapMm;
        if (!facts.TryGetAngleDeg("FNA", out var fna)) return DefaultFunnelGapMm;
        if (!facts.TryGetAngleDeg("HA", out var ha)) return DefaultFunnelGapMm;
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
        var k = DegToRad((double)(ba + ha));

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

    /// <summary>
    /// Calculates the 4516 FG foot-profile values requested by the
    /// SolidWorks model.
    ///
    /// All length inputs/outputs are millimeters. FTA is degrees.
    ///
    /// The caller is responsible for supplying the EFFECTIVE values
    /// that will be sent to SolidWorks after equation overrides.
    ///
    /// For 4516 overlays this means, for example, FL_MAX, GD_MIN or
    /// CD_MIN must be used when those overrides are active. F remains
    /// the DB/reference input because F itself is never sent to SolidWorks.
    ///
    /// Designer rule:
    ///     Split    = (FL - F) / 2
    ///     BR limit = BR * tan(45 - FTA/2) - foot_depth * tan(FTA)
    ///     FRX      = min(Split, FR)
    ///     BRX      = min(Split, BR limit)
    ///     Flat     = FL - FRX - BRX
    ///
    /// Invalid when:
    ///     F >= FL
    ///     BR limit <= 0
    ///     FRX <= 0
    ///     BRX <= 0
    ///     Flat <= 0
    /// </summary>
    public static bool TryCalculate4516FootProfile(
        decimal flMm,
        decimal fMm,
        decimal footDepthMm,
        decimal frMm,
        decimal brMm,
        decimal ftaDeg,
        out decimal frxMm,
        out decimal brxMm,
        out decimal flatMm,
        out decimal splitMm,
        out decimal brLimitMm,
        out string error)
    {
        frxMm = 0m;
        brxMm = 0m;
        flatMm = 0m;
        splitMm = 0m;
        brLimitMm = 0m;
        error = string.Empty;

        if (fMm >= flMm)
        {
            error =
                "F must be smaller than FL. " +
                $"F={fMm} mm, FL={flMm} mm.";

            return false;
        }

        splitMm =
            (flMm - fMm) / 2m;

        var ftaRadians =
            DegToRad((double)ftaDeg);

        var brLimitAngleRadians =
            DegToRad(
                45.0 -
                ((double)ftaDeg / 2.0));

        var brLimitDouble =
            ((double)brMm * Math.Tan(brLimitAngleRadians)) -
            ((double)footDepthMm * Math.Tan(ftaRadians));

        if (!double.IsFinite(brLimitDouble) ||
            brLimitDouble > (double)decimal.MaxValue ||
            brLimitDouble < (double)decimal.MinValue)
        {
            error =
                "BR limit calculation produced an invalid value. " +
                $"BR={brMm} mm, foot_depth={footDepthMm} mm, " +
                $"FTA={ftaDeg} deg.";

            return false;
        }

        brLimitMm =
            (decimal)brLimitDouble;

        if (brLimitMm <= 0m)
        {
            error =
                "BR limit <= 0 (FTA too steep for this foot depth / BR). " +
                $"BR limit={brLimitMm} mm, " +
                $"BR={brMm} mm, " +
                $"foot_depth={footDepthMm} mm, " +
                $"FTA={ftaDeg} deg.";

            return false;
        }

        frxMm =
            Math.Min(
                splitMm,
                frMm);

        brxMm =
            Math.Min(
                splitMm,
                brLimitMm);

        flatMm =
            flMm -
            frxMm -
            brxMm;

        if (frxMm <= 0m ||
            brxMm <= 0m ||
            flatMm <= 0m)
        {
            error =
                "Invalid 4516 foot-profile geometry. " +
                $"FRX={frxMm} mm, " +
                $"BRX={brxMm} mm, " +
                $"Flat={flatMm} mm, " +
                $"Split={splitMm} mm, " +
                $"BR limit={brLimitMm} mm.";

            return false;
        }

        return true;
    }

    public static decimal NonStdCutRawMm(WedgeFacts facts)
    {
        var vr = facts.TryGetMaxLikeMm("VR_MAX", "VR", out var vrMax) ? vrMax : 0m;
        var vrr = facts.TryGetMaxLikeMm("VRR_MAX", "VRR", out var vrrMax) ? vrrMax : 0m;
        Logger.Success($"[EquationGeometry] VR = {vr}mm VRR={vrr}mm VR+VRR={vr + vrr}mm.");
        return vr + vrr;
    }

    public static double OverlayMagnification(WedgeFacts facts, WedgeType wedgeType)
    {
        var source = wedgeType is WedgeType.CKVD or WedgeType.OSG7 or WedgeType._4516
            ? "FL"
            : "T";
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

    public static decimal OverlayReferenceCutMm(double scaleDecimal, WedgeType wedgeType)
    {
        var resolvedScale = scaleDecimal > 0.0 ? (decimal)scaleDecimal : DefaultOverlayScaleDecimal;
        var finalMm = OverlayReferenceCutInch * InchToMm / resolvedScale;

        Logger.Info(
            $"[EquationGeometry] {wedgeType} overlay reference cut: " +
            $"{OverlayReferenceCutInch}in / scale={resolvedScale} -> {finalMm}mm.");

        return finalMm;
    }

    public static decimal RefPointOverlayCutMm(WedgeFacts facts, double scaleDecimal, WedgeType wedgeType)
    {
        if (!facts.HasPositive("VR"))
        {
            var standardCut = OverlayReferenceCutMm(scaleDecimal, wedgeType);

            Logger.Info(
                $"[EquationGeometry] {wedgeType} overlay cut -> " +
                $"VR not present, standard cut={standardCut}mm.");

            return standardCut;
        }

        var rawCut = NonStdCutRawMm(facts);
        var finalCut = OverlaySafeNonStdCutMm(rawCut, scaleDecimal, wedgeType);

        Logger.Info(
            $"[EquationGeometry] {wedgeType} overlay cut -> " +
            $"VR present, raw={rawCut}mm, final={finalCut}mm.");

        return finalCut;
    }

    public static decimal OverlaySafeNonStdCutMm(decimal rawMm, double scaleDecimal, WedgeType wedgeType)
    {
        if (rawMm <= 0m) return 0m;
        if (rawMm <= LargeOverlayVrThresholdMm) return rawMm;

        var resolvedScale = scaleDecimal > 0.0 ? (decimal)scaleDecimal : DefaultOverlayScaleDecimal;
        var finalMm = OverlayReferenceCutInch * InchToMm / resolvedScale;

        Logger.Warn(
            $"[EquationGeometry] {wedgeType} overlay non_std_cut override: " +
            $"raw={rawMm}mm -> {finalMm}mm scale={resolvedScale}");

        return finalMm;
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
}

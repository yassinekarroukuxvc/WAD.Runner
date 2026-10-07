using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules._4516;

public sealed class _4516ToleranceRules : IToleranceRuleSet
{
    public TolerancePlan Build(
        WedgeData wedge,
        DrawingType drawingType,
        WedgeSubclass subclass)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (drawingType != DrawingType.Overlay)
        {
            Logger.Info(
                "[_4516ToleranceRules] Non-overlay drawing -> " +
                "no tolerance updates.");

            return TolerancePlan.Empty;
        }

        var facts =
            new WedgeFacts(wedge, subclass);

        var updates =
            new List<ToleranceUpdate>();

        AddOverlayCutReferencePointUpdates(
            updates,
            facts);

        var hasVrVw =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW");

        var overlayVwCase =
            ResolveOverlayVwCase(
                facts,
                hasVrVw);

        if (subclass == WedgeSubclass.PGB)
        {
            AddPgbOverlayTolerances(
                updates,
                facts,
                hasVrVw,
                overlayVwCase);
        }
        else if (subclass == WedgeSubclass.FG)
        {
            AddFgOverlayTolerances(
                updates,
                facts,
                hasVrVw,
                overlayVwCase);
        }

        Logger.Info(
            $"[_4516ToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}, " +
            $"VR/VW={hasVrVw}, VW case={overlayVwCase}).");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    // ================================================================
    // OVERLAY CUT REFERENCE POINTS
    // ================================================================

    private static void AddOverlayCutReferencePointUpdates(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        // EquationGeometry uses FL as the 4516 magnification source.
        var magnification =
            EquationGeometry.OverlayMagnification(
                facts,
                WedgeType._4516);

        var scale =
            EquationGeometry.OverlayScaleDecimal(
                magnification);

        // Same right/left cut positioning logic as COB.
        var rightCutMm =
            EquationGeometry.RefPointOverlayCutMm(
                facts,
                scale,
                WedgeType._4516);

        var leftCutMm =
            38.1m / (decimal)scale;

        const string rightTarget =
            "ref_point_right@ref_point_right";

        const string leftTarget =
            "ref_point_left@ref_point_left";

        updates.Add(
            new ToleranceUpdate(
                rightTarget,
                rightCutMm,
                ToleranceUnit.LengthMm));

        updates.Add(
            new ToleranceUpdate(
                leftTarget,
                leftCutMm,
                ToleranceUnit.LengthMm));

        Logger.Info(
            "[_4516ToleranceRules] Overlay cut reference points -> " +
            $"VR present={facts.HasPositive("VR")}, " +
            $"magnification={magnification}, " +
            $"scale={scale}, " +
            $"rightCut={rightCutMm} mm -> {rightTarget}, " +
            $"leftCut={leftCutMm} mm -> {leftTarget}.");
    }

    // ================================================================
    // PGB OVERLAY
    // ================================================================

    private static void AddPgbOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        bool hasVrVw,
        OverlayVwCase overlayVwCase)
    {
        var hasPositiveC =
            facts.HasPositive("C");

        AddLengthMinimum(
            updates,
            facts,
            "W",
            "W_MIN@w_case1_overlay_sketch ");

        if (hasPositiveC)
        {
            AddAdjustedTMinimum(
                updates,
                facts,
                "T_MIN@fl_case1_overlay_sketch",
                applyCBackAngleAdjustment: true);

            AddLengthMaximum(
                updates,
                facts,
                "C",
                "C_MAX@fl_case1_overlay_sketch");

            AddLengthMinimum(
                updates,
                facts,
                "FL",
                "FL_MIN@fl_case1_overlay_sketch");
        }
        else
        {
            AddAdjustedTMinimum(
                updates,
                facts,
                "T_MIN@fl_case2_overlay_sketch",
                applyCBackAngleAdjustment: false);
        }

        if (hasVrVw)
        {
            AddVwCaseTolerances(
                updates,
                facts,
                overlayVwCase);
        }
    }

    // ================================================================
    // FG OVERLAY
    // ================================================================

    private static void AddFgOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        bool hasVrVw,
        OverlayVwCase overlayVwCase)
    {
        AddLengthMinimum(
            updates,
            facts,
            "W",
            "W_MIN@w_case2_overlay_sketch");

        AddLengthMaximum(
            updates,
            facts,
            "W",
            "W_MAX@w_case2_overlay_sketch");

        // FG always uses: T_MIN = T_MIN + C_MAX * tan(BA)
        AddAdjustedTMinimum(
            updates,
            facts,
            "T_MIN@fl_case1_overlay_sketch",
            applyCBackAngleAdjustment: true);

        AddLengthMaximum(
            updates,
            facts,
            "C",
            "C_MAX@fl_case1_overlay_sketch");

        AddLengthMinimum(
            updates,
            facts,
            "FL",
            "FL_MIN@fl_case1_overlay_sketch");

        if (hasVrVw)
        {
            AddVwCaseTolerances(
                updates,
                facts,
                overlayVwCase);
        }

        AddFgFootOptionTolerances(
            updates,
            facts);
    }

    // ================================================================
    // VW CASE TOLERANCES
    // ================================================================

    private static void AddVwCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        OverlayVwCase overlayVwCase)
    {
        switch (overlayVwCase)
        {
            case OverlayVwCase.Case1:
                AddLengthMinimum(
                    updates,
                    facts,
                    "VW",
                    "VW_MIN@vw_case1_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    "VR",
                    "VR_MAX@vw_case1_overlay_sketch");

                // D4 is the VRA tolerance dimension in case 1.
                AddAngleMinimum(
                    updates,
                    facts,
                    "VRA",
                    "D4@vw_case1_overlay_sketch");

                break;

            case OverlayVwCase.Case2:
                AddLengthMinimum(
                    updates,
                    facts,
                    "VW",
                    "VW_MIN@vw_case2_overlay_sketch");

                AddLengthMinimum(
                    updates,
                    facts,
                    "W",
                    "W_MIN@vw_case2_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    "VR",
                    "VR_MAX@vw_case2_overlay_sketch");

                // D1 is the VRA tolerance dimension in case 2.
                AddAngleMinimum(
                    updates,
                    facts,
                    "VRA",
                    "D1@vw_case2_overlay_sketch");

                break;

            case OverlayVwCase.None:
            default:
                break;
        }
    }

    // ================================================================
    // FG FOOT-OPTION TOLERANCES
    // ================================================================

    private static void AddFgFootOptionTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        var rawFootOption =
            facts.NormalizedPropertyToken(
                "Wed-Foot_Option",
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Wed-Foot Option",
                "Foot_Option",
                "Foot Option",
                "foot_option");

        var normalizedFootOption =
            NormalizeFootOptionToken(
                rawFootOption);

        var footOption =
            ResolveFootOption(
                facts,
                normalizedFootOption);

        switch (footOption)
        {
            case FootOptionType.Vg:
                AddLengthMaximum(
                    updates,
                    facts,
                    "B",
                    "B_MAX@vg_fg_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    "GD",
                    "GD_MAX@vg_fg_overlay_sketch");

                AddAngleMaximum(
                    updates,
                    facts,
                    "GA",
                    "GA_MAX@vg_fg_overlay_sketch");

                break;

            case FootOptionType.C:
            case FootOptionType.CWithCbr:
                AddLengthMaximum(
                    updates,
                    facts,
                    "CL",
                    "CL_MAX@c_fg_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    "CD",
                    "CD_MAX@c_fg_overlay_sketch");

                break;

            case FootOptionType.G:
                AddLengthMaximum(
                    updates,
                    facts,
                    "GD",
                    "GD_MAX@g_fg_overlay_sketch");

                // Intentional target name from the new 4516 model specification.
                AddLengthMaximum(
                    updates,
                    facts,
                    "GO",
                    "GO_MAX@c_fg_overlay_sketch");

                break;

            case FootOptionType.Cg:
            case FootOptionType.Cc:
            case FootOptionType.Flat:
            default:
                // No overlay tolerance targets were specified for CG, CC or F.
                break;
        }

        Logger.Info(
            "[_4516ToleranceRules] FG foot tolerance selection -> " +
            $"raw='{DisplayToken(rawFootOption)}', " +
            $"normalized='{DisplayToken(normalizedFootOption)}', " +
            $"resolved={footOption}.");
    }

    // ================================================================
    // ADJUSTED T MINIMUM
    // ================================================================

    private static void AddAdjustedTMinimum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string target,
        bool applyCBackAngleAdjustment)
    {
        if (!facts.TryGetLengthBoundsMm(
                "T",
                out var tMinimumMm,
                out _))
        {
            LogMissingBound(
                "T",
                target,
                "length minimum");

            return;
        }

        var finalTMinimumMm =
            tMinimumMm;

        if (applyCBackAngleAdjustment)
        {
            if (!facts.TryGetLengthBoundsMm(
                    "C",
                    out _,
                    out var cMaximumMm))
            {
                Logger.Warn(
                    "[_4516ToleranceRules] Cannot apply T_MIN + C_MAX * tan(BA) " +
                    $"for '{target}' because C_MAX is unavailable. Using T_MIN only.");
            }
            else if (!facts.TryGetAngleDeg(
                         "BA",
                         out var baDegrees))
            {
                Logger.Warn(
                    "[_4516ToleranceRules] Cannot apply T_MIN + C_MAX * tan(BA) " +
                    $"for '{target}' because BA is unavailable. Using T_MIN only.");
            }
            else
            {
                var radians =
                    (double)baDegrees * Math.PI / 180.0;

                var tangent =
                    Math.Tan(radians);

                if (!double.IsFinite(tangent))
                {
                    Logger.Warn(
                        "[_4516ToleranceRules] Cannot apply T_MIN + C_MAX * tan(BA) " +
                        $"for '{target}' because tan(BA) is not finite. Using T_MIN only.");
                }
                else
                {
                    finalTMinimumMm =
                        tMinimumMm +
                        cMaximumMm * (decimal)tangent;
                }
            }
        }

        updates.Add(
            new ToleranceUpdate(
                target,
                finalTMinimumMm,
                ToleranceUnit.LengthMm));

        Logger.Info(
            "[_4516ToleranceRules] T minimum -> " +
            $"target={target}, base={tMinimumMm} mm, " +
            $"adjusted={finalTMinimumMm} mm, " +
            $"apply C/BA adjustment={applyCBackAngleAdjustment}.");
    }

    // ================================================================
    // FOOT OPTION RESOLUTION
    // ================================================================

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts,
        string normalizedFootOption)
    {
        return normalizedFootOption switch
        {
            "LW_VG" or "SW_VG" or "VG" =>
                FootOptionType.Vg,

            "LW_C" or "SW_C" or "C" =>
                HasAllPositiveNominal(
                    facts,
                    "CBRL",
                    "CBRD")
                        ? FootOptionType.CWithCbr
                        : FootOptionType.C,

            "LW_G" or "SW_G" or "G" =>
                FootOptionType.G,

            "LW_CG" or "SW_CG" or "CG" =>
                FootOptionType.Cg,

            "LW_CC" or "SW_CC" or "CC" =>
                FootOptionType.Cc,

            "LW_F" or "SW_F" or "F" =>
                FootOptionType.Flat,

            _ =>
                FootOptionType.Flat
        };
    }

    private static string NormalizeFootOptionToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            RemovePackedDatabaseSuffix(raw)
                .Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .Trim('_')
                .ToUpperInvariant();

        while (token.Contains(
                   "__",
                   StringComparison.Ordinal))
        {
            token =
                token.Replace(
                    "__",
                    "_",
                    StringComparison.Ordinal);
        }

        return token;
    }

    // ================================================================
    // VW CASE RESOLUTION
    // ================================================================

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vwMillimeters) ||
            vwMillimeters <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[_4516ToleranceRules] VW is present but W is " +
                "missing or is not a length. VW case tolerance " +
                "targets were skipped.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        if (vwMillimeters >
            wMillimeters +
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case2;
        }

        Logger.Warn(
            "[_4516ToleranceRules] 4516 overlay received VW < W " +
            $"(VW={vwMillimeters} mm, W={wMillimeters} mm). " +
            "Only VW = W and VW > W are defined. VW case " +
            "tolerance targets were skipped.");

        return OverlayVwCase.None;
    }

    private static bool HasAllPositiveNominal(
        WedgeFacts facts,
        params string[] dimensionKeys)
    {
        foreach (var key in dimensionKeys)
        {
            if (!facts.HasPositive(key))
                return false;
        }

        return true;
    }

    // ================================================================
    // LENGTH BOUNDS
    // ================================================================

    private static void AddLengthMinimum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minimumMillimeters,
                out _))
        {
            LogMissingBound(
                dimensionKey,
                target,
                "length minimum");

            return;
        }

        updates.Add(
            new ToleranceUpdate(
                target,
                minimumMillimeters,
                ToleranceUnit.LengthMm));
    }

    private static void AddLengthMaximum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out _,
                out var maximumMillimeters))
        {
            LogMissingBound(
                dimensionKey,
                target,
                "length maximum");

            return;
        }

        updates.Add(
            new ToleranceUpdate(
                target,
                maximumMillimeters,
                ToleranceUnit.LengthMm));
    }

    // ================================================================
    // ANGLE BOUNDS
    // ================================================================

    private static void AddAngleMinimum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetAngleBoundsDeg(
                dimensionKey,
                out var minimumDegrees,
                out _))
        {
            LogMissingBound(
                dimensionKey,
                target,
                "angle minimum");

            return;
        }

        updates.Add(
            new ToleranceUpdate(
                target,
                minimumDegrees,
                ToleranceUnit.AngleDeg));
    }

    private static void AddAngleMaximum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetAngleBoundsDeg(
                dimensionKey,
                out _,
                out var maximumDegrees))
        {
            LogMissingBound(
                dimensionKey,
                target,
                "angle maximum");

            return;
        }

        updates.Add(
            new ToleranceUpdate(
                target,
                maximumDegrees,
                ToleranceUnit.AngleDeg));
    }

    // ================================================================
    // LOGGING / TOKEN HELPERS
    // ================================================================

    private static void LogMissingBound(
        string dimensionKey,
        string target,
        string boundDescription)
    {
        Logger.Warn(
            "[_4516ToleranceRules] Missing or invalid nominal/" +
            $"tolerance for '{dimensionKey}'. The {boundDescription} " +
            $"target '{target}' was skipped.");
    }

    private static string RemovePackedDatabaseSuffix(
        string raw)
    {
        var token =
            raw
                .Trim()
                .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        if (separatorIndex >= 0)
        {
            token =
                token[..separatorIndex];
        }

        return token;
    }

    private static string DisplayToken(
        string? token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;
    }

    // ================================================================
    // ENUMS
    // ================================================================

    private enum FootOptionType
    {
        Flat,
        Vg,
        C,
        CWithCbr,
        G,
        Cg,
        Cc
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}

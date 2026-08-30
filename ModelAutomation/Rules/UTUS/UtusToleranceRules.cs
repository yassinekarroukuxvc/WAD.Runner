using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.UTUS;

/// <summary>
/// Builds the overlay tolerance updates used by the UTUS model.
///
/// The active SolidWorks tolerance targets depend on Wed-Type:
///     SW_STD      -> std_* overlay sketches
///     SW_180REV   -> rev_* overlay sketches
///
/// Packed database values such as SW_STD;;;;;;; and LW_VG;;;;;;;
/// are cleaned before comparison.
/// </summary>
public sealed class UtusToleranceRules : IToleranceRuleSet
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
                "[UtusToleranceRules] Non-overlay drawing -> " +
                "no tolerance updates.");

            return TolerancePlan.Empty;
        }

        var facts =
            new WedgeFacts(wedge);

        var shank =
            ResolveShankType(facts);

        var updates =
            new List<ToleranceUpdate>();

        AddOverlayToleranceRules(
            updates,
            facts,
            subclass,
            shank);

        Logger.Info(
            $"[UtusToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}, " +
            $"Shank={shank}).");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    // ================================================================
    // OVERLAY RULES
    // ================================================================

    private static void AddOverlayToleranceRules(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        WedgeSubclass subclass,
        UtusShankType shank)
    {
        var prefix =
            shank == UtusShankType.Std
                ? "std"
                : "rev";

        var hasVrVw =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW");

        var vwCase =
            ResolveOverlayVwCase(
                facts,
                hasVrVw);

        var hasVbl =
            facts.HasPositive("VBL");

        var hasRa2 =
            facts.HasPositive("RA2");

        var tCase =
            ResolveOverlayTCase(
                hasVbl,
                hasRa2);

        AddOverlayCutReferencePointUpdates(
            updates,
            facts,
            shank);

        AddSubclassWTolerances(
            updates,
            facts,
            subclass,
            prefix);

        AddVwCaseTolerances(
            updates,
            facts,
            prefix,
            vwCase);

        AddTCaseTolerances(
            updates,
            facts,
            prefix,
            tCase);

        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOptionTolerances(
                updates,
                facts,
                prefix);
        }

        Logger.Info(
            "[UtusToleranceRules] Overlay tolerance selection -> " +
            $"subclass={subclass}, " +
            $"shank={shank}, " +
            $"VBL present={hasVbl}, " +
            $"RA2 present={hasRa2}, " +
            $"VR/VW present={hasVrVw}, " +
            $"VW case={vwCase}, " +
            $"T case={tCase}.");
    }

    // ================================================================
    // OVERLAY CUT REFERENCE POINTS
    // ================================================================

    private static void AddOverlayCutReferencePointUpdates(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        UtusShankType shank)
    {
        var magnification =
            EquationGeometry.OverlayMagnification(
                facts,
                WedgeType.UTUS);

        var scale =
            EquationGeometry.OverlayScaleDecimal(
                magnification);

        // RIGHT reference point: keep the existing UTUS calculation.
        var rightCutMm =
            EquationGeometry.RefPointOverlayCutMm(
                facts,
                scale,
                WedgeType.UTUS);

        // LEFT reference point: always 1.5 inches converted to mm, divided by scale.
        var leftCutMm =
            38.1m / (decimal)scale;

        var rightTarget =
            shank == UtusShankType.Std
                ? "std_ref_point_right@std_ref_point_right"
                : "rev_ref_point_right@rev_ref_point_right";

        var leftTarget =
            shank == UtusShankType.Std
                ? "std_ref_point_left@std_ref_point_left"
                : "rev_ref_point_left@rev_ref_point_left";

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
            "[UtusToleranceRules] Overlay cut reference points -> " +
            $"shank={shank}, " +
            $"VR present={facts.HasPositive("VR")}, " +
            $"magnification={magnification}, " +
            $"scale={scale}, " +
            $"rightCut={rightCutMm} mm -> {rightTarget}, " +
            $"leftCut={leftCutMm} mm -> {leftTarget}.");
    }

    // ================================================================
    // SUBCLASS W TOLERANCES
    // ================================================================

    private static void AddSubclassWTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        WedgeSubclass subclass,
        string prefix)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            AddLengthMinimum(
                updates,
                facts,
                dimensionKey: "W",
                target: $"W_MIN@{prefix}_w_pgb_overlay_sketch");

            return;
        }

        AddLengthMaximum(
            updates,
            facts,
            dimensionKey: "W",
            target: $"W_MAX@{prefix}_w_fg_overlay_sketch");

        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "W",
            target: $"W_MIN@{prefix}_w_fg_overlay_sketch");
    }

    // ================================================================
    // VW CASE TOLERANCES
    // ================================================================

    private static void AddVwCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string prefix,
        OverlayVwCase vwCase)
    {
        switch (vwCase)
        {
            case OverlayVwCase.Case1:
                {
                    var sketch =
                        $"{prefix}_vw_case1_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        dimensionKey: "VW",
                        target: $"VW_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "VR",
                        target: $"VR_MAX@{sketch}");

                    AddAngleMinimum(
                        updates,
                        facts,
                        dimensionKey: "VRA",
                        target: $"VRA_MIN@{sketch}");

                    break;
                }

            case OverlayVwCase.Case2:
                {
                    var sketch =
                        $"{prefix}_vw_case2_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        dimensionKey: "VW",
                        target: $"VW_MIN@{sketch}");

                    AddLengthMinimum(
                        updates,
                        facts,
                        dimensionKey: "W",
                        target: $"W_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "VR",
                        target: $"VR_MAX@{sketch}");

                    AddAngleMinimum(
                        updates,
                        facts,
                        dimensionKey: "ISA",
                        target: $"ISA_MIN@{sketch}");

                    AddAngleMinimum(
                        updates,
                        facts,
                        dimensionKey: "VRA",
                        target: $"VRA_MIN@{sketch}");

                    break;
                }

            case OverlayVwCase.None:
            default:
                break;
        }
    }

    // ================================================================
    // T CASE TOLERANCES
    // ================================================================

    private static void AddTCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string prefix,
        OverlayTCase tCase)
    {
        var sketch =
            tCase switch
            {
                OverlayTCase.Case1 =>
                    $"{prefix}_t_case1_overlay_sketch",

                OverlayTCase.Case2 =>
                    $"{prefix}_t_case2_overlay_sketch",

                OverlayTCase.Case3 =>
                    $"{prefix}_t_case3_overlay_sketch",

                OverlayTCase.Case4 =>
                    $"{prefix}_t_case4_overlay_sketch",

                _ =>
                    null
            };

        if (sketch is null)
            return;

        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "FD",
            target: $"FD_MIN@{sketch}");

        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "T",
            target: $"T_MIN@{sketch}");

        if (tCase is not OverlayTCase.Case3 and
            not OverlayTCase.Case4)
        {
            return;
        }

        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "RA2H",
            target: $"RA2H_MIN@{sketch}");
    }

    // ================================================================
    // FG FOOT-OPTION TOLERANCES
    // ================================================================

    private static void AddFgFootOptionTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string prefix)
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
            NormalizePackedToken(
                rawFootOption);

        var footOption =
            ResolveFootOption(
                facts,
                normalizedFootOption);

        switch (footOption)
        {
            case FootOptionType.C:
            case FootOptionType.CWithCbr:
                {
                    var sketch =
                        $"{prefix}_c_overlay_sketch";

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "CD",
                        target: $"CD_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "CL",
                        target: $"CL_MAX@{sketch}");

                    break;
                }

            case FootOptionType.Vg:
                {
                    var sketch =
                        $"{prefix}_vg_overlay_sketch";

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "B",
                        target: $"B_MAX@{sketch}");

                    AddAngleMaximum(
                        updates,
                        facts,
                        dimensionKey: "GA",
                        target: $"GA_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "GD",
                        target: $"GD_MAX@{sketch}");

                    break;
                }

            case FootOptionType.G:
                {
                    var sketch =
                        $"{prefix}_g_overlay_sketch";

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "GD",
                        target: $"GD_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        dimensionKey: "GO",
                        target: $"GO_MAX@{sketch}");

                    break;
                }

            case FootOptionType.Cc:
            case FootOptionType.Flat:
            default:
                break;
        }

        Logger.Info(
            "[UtusToleranceRules] FG foot tolerance selection -> " +
            $"raw='{DisplayToken(rawFootOption)}', " +
            $"normalized='{DisplayToken(normalizedFootOption)}', " +
            $"resolved={footOption}, prefix={prefix}.");
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
                "[UtusToleranceRules] VW is present but W is missing " +
                "or is not a length. VW case tolerance targets " +
                "were skipped.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        return OverlayVwCase.Case2;
    }

    // ================================================================
    // T CASE RESOLUTION
    // ================================================================

    private static OverlayTCase ResolveOverlayTCase(
        bool hasVbl,
        bool hasRa2)
    {
        if (hasVbl && hasRa2)
            return OverlayTCase.Case4;

        if (hasRa2)
            return OverlayTCase.Case3;

        if (hasVbl)
            return OverlayTCase.Case2;

        return OverlayTCase.Case1;
    }

    // ================================================================
    // SHANK RESOLUTION
    // ================================================================

    private static UtusShankType ResolveShankType(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Wedge-Type",
                "Wedge_Type",
                "wedge_type");

        var token =
            NormalizePackedToken(raw);

        return token switch
        {
            "SW_STD" or "STD" =>
                UtusShankType.Std,

            "SW_180REV" or "SW_180_REV" or "180REV" or "180_REV" =>
                UtusShankType.Rev,

            _ =>
                throw new InvalidOperationException(
                    "Unable to resolve the UTUS shank from 'Wed-Type'. " +
                    "Expected SW_STD or SW_180REV, but received " +
                    $"'{DisplayToken(token)}'.")
        };
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
                facts.HasPositive("CBRL") && facts.HasPositive("CBRD")
                    ? FootOptionType.CWithCbr
                    : FootOptionType.C,

            "LW_G" or "SW_G" or "G" =>
                FootOptionType.G,

            "LW_CC" or "SW_CC" or "CC" =>
                FootOptionType.Cc,

            "LW_FLAT" or "SW_FLAT" =>
                FootOptionType.Flat,

            _ =>
                FootOptionType.Flat
        };
    }

    // ================================================================
    // DIMENSION HELPERS
    // ================================================================

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
            "[UtusToleranceRules] Missing or invalid nominal/" +
            $"tolerance for '{dimensionKey}'. The {boundDescription} " +
            $"target '{target}' was skipped.");
    }

    private static string NormalizePackedToken(
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

    private enum UtusShankType
    {
        Std,
        Rev
    }

    private enum FootOptionType
    {
        Flat,
        Vg,
        C,
        CWithCbr,
        G,
        Cc
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }

    private enum OverlayTCase
    {
        Case1,
        Case2,
        Case3,
        Case4
    }
}
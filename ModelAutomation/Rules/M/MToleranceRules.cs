using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.M;

public sealed class MToleranceRules : IToleranceRuleSet
{
    public TolerancePlan Build(
        WedgeData wedge,
        DrawingType drawingType,
        WedgeSubclass subclass)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (drawingType != DrawingType.Overlay)
            return TolerancePlan.Empty;

        var facts =
            new WedgeFacts(wedge);

        var shank =
            ResolveShankType(
                facts);

        var prefix =
            shank == MShankType.Std
                ? "std"
                : "rev";

        var updates =
            new List<ToleranceUpdate>();

        AddOverlayCutReferencePointUpdates(
            updates,
            facts,
            shank);

        AddSubclassWTolerances(
            updates,
            facts,
            subclass,
            prefix);

        var hasVrVw =
            HasAllPositive(
                facts,
                "VR",
                "VW");

        AddVwCaseTolerances(
            updates,
            facts,
            prefix,
            ResolveOverlayVwCase(
                facts,
                hasVrVw));

        AddTCaseTolerances(
            updates,
            facts,
            prefix,
            ResolveOverlayTCase(
                facts.HasPositive("VBL"),
                facts.HasPositive("RA2")));

        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOptionTolerances(
                updates,
                facts,
                prefix);
        }

        Logger.Info(
            $"[MToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}, " +
            $"Shank={shank}).");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    private static void AddOverlayCutReferencePointUpdates(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        MShankType shank)
    {
        var magnification =
            EquationGeometry.OverlayMagnification(
                facts,
                WedgeType.M);

        var scale =
            EquationGeometry.OverlayScaleDecimal(
                magnification);

        var cutMm =
            EquationGeometry.RefPointOverlayCutMm(
                facts,
                scale,
                WedgeType.M);

        var targets =
            shank == MShankType.Std
                ? new[]
                {
                    "std_ref_point_right@std_ref_point_right",
                    "std_ref_point_left@std_ref_point_left"
                }
                : new[]
                {
                    "rev_ref_point_right@rev_ref_point_right",
                    "rev_ref_point_left@rev_ref_point_left"
                };

        foreach (var target in targets)
        {
            updates.Add(
                new ToleranceUpdate(
                    target,
                    cutMm,
                    ToleranceUnit.LengthMm));
        }
    }

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
                "W",
                $"W_MIN@{prefix}_w_pgb_overlay_sketch");

            return;
        }

        AddLengthMaximum(
            updates,
            facts,
            "W",
            $"W_MAX@{prefix}_w_fg_overlay_sketch");

        AddLengthMinimum(
            updates,
            facts,
            "W",
            $"W_MIN@{prefix}_w_fg_overlay_sketch");
    }

    private static void AddVwCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string prefix,
        OverlayVwCase vwCase)
    {
        if (vwCase == OverlayVwCase.None)
            return;

        var sketch =
            vwCase == OverlayVwCase.Case1
                ? $"{prefix}_vw_case1_overlay_sketch"
                : $"{prefix}_vw_case2_overlay_sketch";

        AddLengthMinimum(
            updates,
            facts,
            "VW",
            $"VW_MIN@{sketch}");

        AddLengthMaximum(
            updates,
            facts,
            "VR",
            $"VR_MAX@{sketch}");

        AddAngleMinimum(
            updates,
            facts,
            "VRA",
            $"VRA_MIN@{sketch}");

        if (vwCase ==
            OverlayVwCase.Case2)
        {
            AddLengthMinimum(
                updates,
                facts,
                "W",
                $"W_MIN@{sketch}");

            AddAngleMinimum(
                updates,
                facts,
                "ISA",
                $"ISA_MIN@{sketch}");
        }
    }

    private static void AddTCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string prefix,
        OverlayTCase tCase)
    {
        switch (tCase)
        {
            case OverlayTCase.Case1:
                {
                    var sketch =
                        $"{prefix}_t_case1_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "FL",
                        $"FL_MIN@{sketch}");

                    AddLengthMinimum(
                        updates,
                        facts,
                        "T",
                        $"T_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "ND",
                        $"ND_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "C",
                        $"C_MAX@{sketch}");

                    break;
                }

            case OverlayTCase.Case2:
                {
                    var sketch =
                        $"{prefix}_t_case2_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "FL",
                        $"FL_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "ND",
                        $"ND_MAX@{sketch}");

                    AddLengthMinimum(
                        updates,
                        facts,
                        "T",
                        $"T_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "C",
                        $"C_MAX@{sketch}");

                    break;
                }

            case OverlayTCase.Case3:
                {
                    var sketch =
                        $"{prefix}_t_case3_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "T",
                        $"T_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "ND",
                        $"ND_MAX@{prefix}_t_case2_overlay_sketch");

                    AddLengthMinimum(
                        updates,
                        facts,
                        "FL",
                        $"FL_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "RA2H",
                        $"RA2H_MAX@{sketch}");

                    break;
                }

            case OverlayTCase.Case4:
                {
                    var sketch =
                        $"{prefix}_t_case4_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "T",
                        $"T_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "ND",
                        $"ND_MAX@{prefix}_t_case2_overlay_sketch");

                    AddLengthMinimum(
                        updates,
                        facts,
                        "FL",
                        $"FL_MIN@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "RA2H",
                        $"RA2H_MAX@{sketch}");

                    break;
                }
        }
    }

    private static void AddFgFootOptionTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string prefix)
    {
        var footOption =
            ResolveFootOption(
                facts);

        switch (footOption)
        {
            case FootOptionType.C:
                {
                    var sketch =
                        $"{prefix}_c_overlay_sketch";

                    AddLengthMaximum(
                        updates,
                        facts,
                        "CD",
                        $"CD_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "CL",
                        $"CL_MAX@{sketch}");

                    break;
                }

            case FootOptionType.Vg:
                {
                    var sketch =
                        $"{prefix}_vg_overlay_sketch";

                    AddLengthMaximum(
                        updates,
                        facts,
                        "B",
                        $"B_MAX@{sketch}");

                    AddAngleMaximum(
                        updates,
                        facts,
                        "GA",
                        $"GA_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "GD",
                        $"GD_MAX@{sketch}");

                    break;
                }

            case FootOptionType.G:
                {
                    var sketch =
                        $"{prefix}_g_overlay_sketch";

                    AddLengthMaximum(
                        updates,
                        facts,
                        "GD",
                        $"GD_MAX@{sketch}");

                    AddLengthMaximum(
                        updates,
                        facts,
                        "GO",
                        $"GO_MAX@{sketch}");

                    break;
                }

            case FootOptionType.F:
                // F intentionally has no foot-option overlay
                // sketch and therefore no foot-option overlay tolerances.
                break;

            case FootOptionType.Cc:
            case FootOptionType.Unknown:
            default:
                break;
        }
    }

    private static MShankType ResolveShankType(
        WedgeFacts facts)
    {
        var token =
            NormalizePackedToken(
                facts.NormalizedPropertyToken(
                    "Wed-Type",
                    "Wed_Type",
                    "Wed Type",
                    "Wedge-Type",
                    "Wedge_Type",
                    "wedge_type"));

        return token switch
        {
            "SW_STD" or "STD" =>
                MShankType.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                MShankType.Rev,

            _ =>
                throw new InvalidOperationException(
                    "Unable to resolve the M shank from 'Wed-Type'. " +
                    "Expected SW_STD or SW_180REV, but received " +
                    $"'{DisplayToken(token)}'.")
        };
    }

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts)
    {
        var token =
            NormalizePackedToken(
                facts.NormalizedPropertyToken(
                    "Wed-Foot_Option",
                    "Wed_Foot_Option",
                    "Wed Foot Option",
                    "Wed-Foot Option",
                    "Foot_Option",
                    "Foot Option",
                    "foot_option"));

        return token switch
        {
            "LW_C" or
            "SW_C" or
            "C" =>
                FootOptionType.C,

            "LW_VG" or
            "SW_VG" or
            "VG" =>
                FootOptionType.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                FootOptionType.G,

            "LW_F" or
            "SW_F" or
            "F" =>
                FootOptionType.F,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                FootOptionType.Cc,

            _ =>
                FootOptionType.Unknown
        };
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vw) ||
            vw <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var w))
        {
            Logger.Warn(
                "[MToleranceRules] VW is present but W is missing/not a length. " +
                "VW tolerances skipped.");

            return OverlayVwCase.None;
        }

        return decimal.Abs(vw - w) <=
               WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVwCase.Case1
            : OverlayVwCase.Case2;
    }

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

    private static bool HasAllPositive(
        WedgeFacts facts,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!facts.HasPositive(key))
                return false;
        }

        return true;
    }

    private static void AddLengthMinimum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minMm,
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
                minMm,
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
                out var maxMm))
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
                maxMm,
                ToleranceUnit.LengthMm));
    }

    private static void AddAngleMinimum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetAngleBoundsDeg(
                dimensionKey,
                out var minDeg,
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
                minDeg,
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
                out var maxDeg))
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
                maxDeg,
                ToleranceUnit.AngleDeg));
    }

    private static void LogMissingBound(
        string dimensionKey,
        string target,
        string bound)
    {
        Logger.Warn(
            $"[MToleranceRules] Missing/invalid nominal/tolerance for '{dimensionKey}'. " +
            $"The {bound} target '{target}' was skipped.");
    }

    private static string NormalizePackedToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

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

        token =
            token
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

    private static string DisplayToken(
        string token)
        => string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;

    private enum MShankType
    {
        Std,
        Rev
    }

    private enum FootOptionType
    {
        Unknown,
        C,
        Vg,
        G,
        F,
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
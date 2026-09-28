using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.VM;

public sealed class VMToleranceRules : IToleranceRuleSet
{
    private const string Prefix = "std";

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
            new WedgeFacts(
                wedge,
                subclass);

        var updates =
            new List<ToleranceUpdate>();

        AddSubclassWTolerances(
            updates,
            facts,
            subclass);

        var hasVrVw =
            HasAllPositive(
                facts,
                "VR",
                "VW");

        AddVwCaseTolerances(
            updates,
            facts,
            ResolveOverlayVwCase(
                facts,
                hasVrVw));

        AddTCaseTolerances(
            updates,
            facts,
            ResolveOverlayTCase(
                facts.HasPositive("VBL"),
                facts.HasPositive("RA2")));

        if (subclass == WedgeSubclass.FG)
        {
            AddFgFootOptionTolerances(
                updates,
                facts);
        }

        Logger.Info(
            $"[VMToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}).");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    private static void AddSubclassWTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            /*
             * PGB:
             * W_MIN@std_w_pgb_overlay_sketch
             */
            AddLengthMinimum(
                updates,
                facts,
                "W",
                "W_MIN@std_w_pgb_overlay_sketch");

            return;
        }

        if (subclass == WedgeSubclass.FG)
        {
            /*
             * FG:
             * W_MAX@std_w_fg_overlay_sketch
             * W_MIN@std_w_fg_overlay_sketch
             */
            AddLengthMaximum(
                updates,
                facts,
                "W",
                "W_MAX@std_w_fg_overlay_sketch");

            AddLengthMinimum(
                updates,
                facts,
                "W",
                "W_MIN@std_w_fg_overlay_sketch");
        }
    }

    private static void AddVwCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        OverlayVwCase vwCase)
    {
        switch (vwCase)
        {
            /*
             * Case 1:
             * VR > 0
             * VW > 0
             * VW = W
             *
             * VW_MIN@std_vw_case1_overlay_sketch
             * VR_MAX@std_vw_case1_overlay_sketch
             * VRA_MIN@std_vw_case1_overlay_sketch
             * ISA_MIN@std_vw_case1_overlay_sketch
             */
            case OverlayVwCase.Case1:
                {
                    const string sketch =
                        "std_vw_case1_overlay_sketch";

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

                    AddAngleMinimum(
                        updates,
                        facts,
                        "ISA",
                        $"ISA_MIN@{sketch}");

                    break;
                }

            /*
             * Case 2:
             * VR > 0
             * VW > 0
             * VW != W
             *
             * D1@std_vw_case2_overlay_sketch
             *     <- value of VW_MIN
             *
             * W_MIN@std_vw_case2_overlay_sketch
             *
             * ISA_MIN@std_vw_case2_overlay_sketch
             *
             * VRA@std_vw_case2_overlay_sketch
             *     <- value of VRA_MIN
             */
            case OverlayVwCase.Case2:
                {
                    const string sketch =
                        "std_vw_case2_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "VW",
                        $"D1@{sketch}");

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

                    AddAngleMinimum(
                        updates,
                        facts,
                        "VRA",
                        $"VRA@{sketch}");

                    break;
                }

            case OverlayVwCase.None:
            default:
                break;
        }
    }

    private static void AddTCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        OverlayTCase tCase)
    {
        switch (tCase)
        {
            /*
             * Case 1:
             * no VBL
             * no RA2
             */
            case OverlayTCase.Case1:
                {
                    const string sketch =
                        "std_t_case1_overlay_sketch";

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

            /*
             * Case 2:
             * VBL > 0
             * RA2 <= 0
             */
            case OverlayTCase.Case2:
                {
                    const string sketch =
                        "std_t_case2_overlay_sketch";

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

            /*
             * Case 3:
             * VBL <= 0
             * RA2 > 0
             */
            case OverlayTCase.Case3:
                {
                    const string sketch =
                        "std_t_case3_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "T",
                        $"T_MIN@{sketch}");

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

                    AddLengthMaximum(
                        updates,
                        facts,
                        "ND",
                        $"ND_MAX@{sketch}");

                    break;
                }

            /*
             * Case 4:
             * VBL > 0
             * RA2 > 0
             */
            case OverlayTCase.Case4:
                {
                    const string sketch =
                        "std_t_case4_overlay_sketch";

                    AddLengthMinimum(
                        updates,
                        facts,
                        "T",
                        $"T_MIN@{sketch}");

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

                    AddLengthMaximum(
                        updates,
                        facts,
                        "ND",
                        $"ND_MAX@{sketch}");

                    break;
                }
        }
    }

    private static void AddFgFootOptionTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        var footOption =
            ResolveFootOption(
                facts);

        switch (footOption)
        {
            /*
             * C and C with CBR use the same C overlay sketch.
             *
             * CD_MAX@std_c_overlay_sketch
             * CL_MAX@std_c_overlay_sketch
             */
            case FootOptionType.C:
            case FootOptionType.CWithCbr:
                {
                    const string sketch =
                        "std_c_overlay_sketch";

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

            /*
             * VG:
             *
             * B_MAX@std_vg_overlay_sketch
             * GA_MAX@std_vg_overlay_sketch
             * GD_MAX@std_vg_overlay_sketch
             */
            case FootOptionType.Vg:
                {
                    const string sketch =
                        "std_vg_overlay_sketch";

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

            /*
             * G:
             *
             * GD_MAX@std_g_overlay_sketch
             * GO_MAX@std_g_overlay_sketch
             */
            case FootOptionType.G:
                {
                    const string sketch =
                        "std_g_overlay_sketch";

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

            /*
             * F has no foot-option overlay sketch/tolerances.
             */
            case FootOptionType.F:
            case FootOptionType.Unknown:
            default:
                break;
        }
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
                facts.HasPositive("CBRL") &&
                facts.HasPositive("CBRD")
                    ? FootOptionType.CWithCbr
                    : FootOptionType.C,

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
                "[VMToleranceRules] VW is present but W is missing/not a length. " +
                "VW tolerances skipped.");

            return OverlayVwCase.None;
        }

        return decimal.Abs(
                   vw -
                   w) <=
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
            $"[VMToleranceRules] Missing/invalid nominal/tolerance for '{dimensionKey}'. " +
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

    private enum FootOptionType
    {
        Unknown,
        C,
        CWithCbr,
        Vg,
        G,
        F
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

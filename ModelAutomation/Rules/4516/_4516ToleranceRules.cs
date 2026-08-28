using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
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

        /*
         * The 4516 tolerance rules currently apply only to Overlay.
         * Production and Customer drawings receive no tolerance
         * updates from this rule set.
         */
        if (drawingType != DrawingType.Overlay)
        {
            Logger.Info(
                "[_4516ToleranceRules] Non-overlay drawing -> " +
                "no tolerance updates.");

            return TolerancePlan.Empty;
        }

        var facts =
            new WedgeFacts(wedge);

        var updates =
            new List<ToleranceUpdate>();

        AddOverlayToleranceRules(
            updates,
            facts,
            subclass);

        Logger.Info(
            $"[_4516ToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}).");

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
        WedgeSubclass subclass)
    {
        /*
         * The VR/VW overlay family is considered active only when
         * both VR and VW have positive nominal values.
         */
        var hasVrVw =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW");

        var overlayVwCase =
            ResolveOverlayVwCase(
                facts,
                hasVrVw);

        /*
         * FL is routed to the SLB sketch when VBL > 0.
         * Missing, zero or negative VBL uses the normal FL sketch.
         */
        var hasSlb =
            facts.HasPositive("VBL");

        if (subclass == WedgeSubclass.PGB)
        {
            AddPgbOverlayTolerances(
                updates,
                facts,
                hasSlb,
                hasVrVw,
                overlayVwCase);
        }
        else
        {
            AddFgOverlayTolerances(
                updates,
                facts,
                hasSlb,
                hasVrVw,
                overlayVwCase);
        }

        Logger.Info(
            "[_4516ToleranceRules] Overlay tolerance selection -> " +
            $"subclass={subclass}, " +
            $"VBL present={hasSlb}, " +
            $"VR/VW present={hasVrVw}, " +
            $"VW case={overlayVwCase}.");
    }

    // ================================================================
    // PGB OVERLAY
    // ================================================================

    private static void AddPgbOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        bool hasSlb,
        bool hasVrVw,
        OverlayVwCase overlayVwCase)
    {
        /*
         * Common PGB overlay tolerance values.
         */
        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "W",
            target: "W_MIN@w_pgb_overlay_sketch");

        if (hasSlb)
        {
            AddLengthMinimum(
                updates,
                facts,
                dimensionKey: "FL",
                target: "FL_MIN@slb_pgb_overlay_sketch");
        }
        else
        {
            AddLengthMinimum(
                updates,
                facts,
                dimensionKey: "FL",
                target: "FL_MIN@fl_pgb_overlay_sketch");
        }

        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "T",
            target: "T_MIN@fl_pgb_overlay_sketch");

        AddLengthMaximum(
            updates,
            facts,
            dimensionKey: "C",
            target: "C_MAX@fl_pgb_overlay_sketch");

        if (!hasVrVw)
            return;

        switch (overlayVwCase)
        {
            case OverlayVwCase.Case1:
                AddVwCaseTolerances(
                    updates,
                    facts,
                    sketchName:
                        "vw_case1_pgb_overlay_sketch",
                    includeIsa: false);

                break;

            case OverlayVwCase.Case2:
                AddVwCaseTolerances(
                    updates,
                    facts,
                    sketchName:
                        "vw_case2_pgb_overlay_sketch",
                    includeIsa: true);

                break;

            case OverlayVwCase.None:
            default:
                break;
        }
    }

    // ================================================================
    // FG OVERLAY
    // ================================================================

    private static void AddFgOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        bool hasSlb,
        bool hasVrVw,
        OverlayVwCase overlayVwCase)
    {
        /*
         * Common FG overlay tolerance values.
         */
        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "W",
            target: "W_MIN@w_fg_overlay_sketch");

        AddLengthMaximum(
            updates,
            facts,
            dimensionKey: "W",
            target: "W_MAX@w_fg_overlay_sketch");

        if (hasSlb)
        {
            AddLengthMinimum(
                updates,
                facts,
                dimensionKey: "FL",
                target: "FL_MIN@slb_fg_overlay_sketch");
        }
        else
        {
            AddLengthMinimum(
                updates,
                facts,
                dimensionKey: "FL",
                target: "FL_MIN@fl_fg_overlay_sketch");
        }

        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "T",
            target: "T_MIN@fl_fg_overlay_sketch");

        AddLengthMaximum(
            updates,
            facts,
            dimensionKey: "C",
            target: "C_MAX@fl_fg_overlay_sketch");

        if (hasVrVw)
        {
            switch (overlayVwCase)
            {
                case OverlayVwCase.Case1:
                    AddVwCaseTolerances(
                        updates,
                        facts,
                        sketchName:
                            "vw_case1_fg_overlay_sketch",
                        includeIsa: false);

                    break;

                case OverlayVwCase.Case2:
                    AddVwCaseTolerances(
                        updates,
                        facts,
                        sketchName:
                            "vw_case2_fg_overlay_sketch",
                        includeIsa: true);

                    break;

                case OverlayVwCase.None:
                default:
                    break;
            }
        }

        AddFgFootOptionTolerances(
            updates,
            facts);
    }

    // ================================================================
    // VR / VW CASE TOLERANCES
    // ================================================================

    private static void AddVwCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string sketchName,
        bool includeIsa)
    {
        AddLengthMinimum(
            updates,
            facts,
            dimensionKey: "VW",
            target: $"VW_MIN@{sketchName}");

        /*
         * The 4516 equation override uses VR_MIN.
         * The corresponding overlay sketch tolerance uses VR_MAX.
         */
        AddLengthMaximum(
            updates,
            facts,
            dimensionKey: "VR",
            target: $"VR_MAX@{sketchName}");

        AddAngleMinimum(
            updates,
            facts,
            dimensionKey: "VRA",
            target: $"VRA_MIN@{sketchName}");

        if (!includeIsa)
            return;

        AddAngleMinimum(
            updates,
            facts,
            dimensionKey: "ISA",
            target: $"ISA_MIN@{sketchName}");
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
                    dimensionKey: "B",
                    target:
                        "B_MAX@vg_fg_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    dimensionKey: "GD",
                    target:
                        "GD_MAX@vg_fg_overlay_sketch");

                AddAngleMaximum(
                    updates,
                    facts,
                    dimensionKey: "GA",
                    target:
                        "GA_MAX@vg_fg_overlay_sketch");

                break;

            case FootOptionType.C:
            case FootOptionType.CWithCbr:
                AddLengthMaximum(
                    updates,
                    facts,
                    dimensionKey: "CL",
                    target:
                        "CL_MAX@c_fg_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    dimensionKey: "CD",
                    target:
                        "CD_MAX@c_fg_overlay_sketch");

                break;

            case FootOptionType.G:
                AddLengthMaximum(
                    updates,
                    facts,
                    dimensionKey: "GD",
                    target:
                        "GD_MAX@g_fg_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    dimensionKey: "GO",
                    target:
                        "GO_MAX@g_fg_overlay_sketch");

                break;

            case FootOptionType.Flat:
            case FootOptionType.Cc:
            default:
                /*
                 * No 4516 overlay tolerance targets were specified
                 * for flat or CC foot options.
                 */
                break;
        }

        Logger.Info(
            "[_4516ToleranceRules] FG foot tolerance selection -> " +
            $"raw='{DisplayToken(rawFootOption)}', " +
            $"normalized='{DisplayToken(normalizedFootOption)}', " +
            $"resolved={footOption}.");
    }

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts,
        string normalizedFootOption)
    {
        switch (normalizedFootOption)
        {
            case "LW_VG":
            case "SW_VG":
                return FootOptionType.Vg;

            case "LW_C":
            case "SW_C":
                return HasAllPositiveNominal(
                    facts,
                    "CBRL",
                    "CBRD")
                        ? FootOptionType.CWithCbr
                        : FootOptionType.C;

            case "LW_G":
            case "SW_G":
                return FootOptionType.G;

            case "LW_CC":
            case "SW_CC":
                return FootOptionType.Cc;

            case "LW_FLAT":
            case "SW_FLAT":
                return FootOptionType.Flat;

            /*
             * Empty and unknown values are treated as flat.
             */
            default:
                return FootOptionType.Flat;
        }
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
        Cc
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

public sealed class CkvdToleranceRules : IToleranceRuleSet
{
    public TolerancePlan Build(
        WedgeData wedge,
        DrawingType drawingType,
        WedgeSubclass subclass)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        var facts = new WedgeFacts(wedge, subclass);
        var updates = new List<ToleranceUpdate>();

        /*
         * CKVD now uses only the new overlay tolerance rules.
         *
         * The legacy CKVD targets have been removed:
         * - VR_MIN@FG_Wed_VW / VR_MAX@FG_Wed_VW
         * - UTOL/LTOL@PGB_Wed_*
         * - UTOL/LTOL@FG_Wed_*
         * - VW_UTOL/VW_LTOL@FG_Wed_VW
         *
         * Production and Customer drawings therefore receive no
         * tolerance updates from this rule set.
         */
        if (drawingType == DrawingType.Overlay)
        {
            AddOverlayToleranceRules(
                updates,
                facts,
                subclass);
        }

        Logger.Info(
            $"[CkvdToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}).");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    private static void AddOverlayToleranceRules(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        var style = ResolveStyle(
            facts,
            subclass);

        var vrPositive =
            facts.HasPositive("VR");

        var vwPositive =
            facts.HasPositive("VW");

        var hasVrAndVw =
            vrPositive &&
            vwPositive;

        var hasNeitherVrNorVw =
            !vrPositive &&
            !vwPositive;

        var overlayVwCase = ResolveOverlayVwCase(
            facts,
            hasVrAndVw);

        if (subclass == WedgeSubclass.PGB)
        {
            AddPgbOverlayTolerances(
                updates,
                facts,
                style,
                hasVrAndVw,
                hasNeitherVrNorVw,
                overlayVwCase);
        }
        else
        {
            AddFgOverlayTolerances(
                updates,
                facts,
                style,
                hasVrAndVw,
                overlayVwCase);
        }

        Logger.Info(
            "[CkvdToleranceRules] Overlay tolerance selection -> " +
            $"subclass={subclass}, style={style}, " +
            $"VR>0={vrPositive}, VW>0={vwPositive}, " +
            $"VW case={overlayVwCase}.");
    }

    private static void AddPgbOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        CkvdStyle style,
        bool hasVrAndVw,
        bool hasNeitherVrNorVw,
        OverlayVwCase overlayVwCase)
    {
        var styleFlSketch =
            style == CkvdStyle.StyleA
                ? "style_a_fl_pgb_overlay_sketch"
                : "style_b_fl_pgb_overlay_sketch";

        /*
         * PGB equation/tolerance pairing:
         *
         * VR = 0 and VW = 0:
         *   Equation W  = W_MIN  -> tolerance W_MAX
         *   Equation FL = FL_MIN -> tolerance FL_MAX
         *
         * VR > 0 and VW > 0:
         *   Equation FL  = FL_MIN -> tolerance FL_MAX
         *   Equation VW  = VW_MAX -> tolerance VW_MIN
         *   Equation VR  = VR_MAX -> tolerance VR_MIN
         *   Equation VRA = VRA_MAX -> tolerance VRA_MIN
         *
         *   VW = W:
         *     Equation W = W_MIN -> tolerance W_MAX
         *
         *   VW > W:
         *     Equation W   = W_MAX   -> tolerance W_MIN
         *     Equation ISA = ISA_MAX -> tolerance ISA_MIN
         */
        if (hasNeitherVrNorVw)
        {
            AddLengthMaximum(
                updates,
                facts,
                "W",
                "W_MAX@w_pgb_overlay_sketch");

            AddLengthMaximum(
                updates,
                facts,
                "FL",
                $"FL_MAX@{styleFlSketch}");

            return;
        }

        if (!hasVrAndVw)
        {
            Logger.Warn(
                "[CkvdToleranceRules] CKVD PGB overlay has only one " +
                "of VR/VW positive. No VR/VW-dependent PGB tolerance " +
                "targets are defined for this combination.");

            return;
        }

        AddLengthMaximum(
            updates,
            facts,
            "FL",
            $"FL_MAX@{styleFlSketch}");

        switch (overlayVwCase)
        {
            case OverlayVwCase.Case1:
                AddVwCaseMinimums(
                    updates,
                    facts,
                    "vw_case1_pgb_overlay_sketch",
                    includeWAndIsa: false);

                AddLengthMaximum(
                    updates,
                    facts,
                    "W",
                    "W_MAX@vw_case1_pgb_overlay_sketch");
                break;

            case OverlayVwCase.Case2:
                AddVwCaseMinimums(
                    updates,
                    facts,
                    "vw_case2_pgb_overlay_sketch",
                    includeWAndIsa: true);
                break;
        }
    }

    private static void AddFgOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        CkvdStyle style,
        bool hasVrAndVw,
        OverlayVwCase overlayVwCase)
    {
        var styleFlSketch =
            style == CkvdStyle.StyleA
                ? "style_a_fl_fg_overlay_sketch"
                : "style_b_fl_fg_overlay_sketch";

        /*
         * These FG targets are required in both the no-VR-family and
         * with-VR-family overlay cases.
         */
        AddLengthMaximum(
            updates,
            facts,
            "B",
            "B_MAX@vg_fg_overaly_sketch");

        AddAngleMaximum(
            updates,
            facts,
            "GA",
            "GA_MAX@vg_fg_overaly_sketch");

        AddLengthMaximum(
            updates,
            facts,
            "GD",
            "GD_MAX@vg_fg_overaly_sketch");

        AddLengthMinimum(
            updates,
            facts,
            "W",
            "W_MIN@w_fg_overlay_sketch");

        AddLengthMaximum(
            updates,
            facts,
            "W",
            "W_MAX@w_fg_overlay_sketch");

        AddLengthMinimum(
            updates,
            facts,
            "FL",
            $"FL_MIN@{styleFlSketch}");

        AddLengthMaximum(
            updates,
            facts,
            "FL",
            $"FL_MAX@{styleFlSketch}");

        if (!hasVrAndVw)
            return;

        switch (overlayVwCase)
        {
            case OverlayVwCase.Case1:
                AddVwCaseMinimums(
                    updates,
                    facts,
                    "vw_case1_fg_overlay_sketch",
                    includeWAndIsa: false);
                break;

            case OverlayVwCase.Case2:
                AddVwCaseMinimums(
                    updates,
                    facts,
                    "vw_case2_fg_overlay_sketch",
                    includeWAndIsa: true);
                break;
        }
    }

    private static void AddVwCaseMinimums(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string sketchName,
        bool includeWAndIsa)
    {
        AddLengthMinimum(
            updates,
            facts,
            "VW",
            $"VW_MIN@{sketchName}");

        AddLengthMinimum(
            updates,
            facts,
            "VR",
            $"VR_MIN@{sketchName}");

        AddAngleMinimum(
            updates,
            facts,
            "VRA",
            $"VRA_MIN@{sketchName}");

        if (!includeWAndIsa)
            return;

        AddLengthMinimum(
            updates,
            facts,
            "W",
            $"W_MIN@{sketchName}");

        AddAngleMinimum(
            updates,
            facts,
            "ISA",
            $"ISA_MIN@{sketchName}");
    }

    private static void AddLengthMinimum(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string target)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minimumMm,
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
                minimumMm,
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
                out var maximumMm))
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
                maximumMm,
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

    private static void LogMissingBound(
        string dimensionKey,
        string target,
        string boundDescription)
    {
        Logger.Warn(
            "[CkvdToleranceRules] Missing/invalid nominal or tolerance " +
            $"for '{dimensionKey}'. The {boundDescription} target " +
            $"'{target}' was skipped.");
    }

    private static CkvdStyle ResolveStyle(
        WedgeFacts facts,
        WedgeSubclass subclass)
    {
        if (facts is null)
            throw new ArgumentNullException(nameof(facts));

        string propertyName;
        string? raw;

        if (subclass == WedgeSubclass.PGB)
        {
            propertyName = "PGB-Type";

            raw = facts.NormalizedSubclassPropertyToken(
                "PGB-Type",
                "PGB_Type",
                "PGB Type");
        }
        else
        {
            propertyName = "Wed-Type";

            raw = facts.NormalizedSubclassPropertyToken(
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Shank_Type",
                "shank_type");
        }

        if (string.Equals(
                raw,
                "LW_STYLE_A_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdStyle.StyleA;
        }

        if (string.Equals(
                raw,
                "LW_STYLE_B_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdStyle.StyleB;
        }

        throw new InvalidOperationException(
            $"Unable to resolve the CKVD shank style for {subclass} from '{propertyName}'. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{(string.IsNullOrWhiteSpace(raw) ? "<missing>" : raw)}'.");
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasVrAndVw)
    {
        if (!hasVrAndVw ||
            !facts.TryGetLengthMm(
                "VW",
                out var vwMillimeters) ||
            vwMillimeters <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[CkvdToleranceRules] VW is present but W is missing or " +
                "not a length. CKVD VW case tolerances were skipped.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        if (vwMillimeters >
            wMillimeters + WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case2;
        }

        Logger.Warn(
            "[CkvdToleranceRules] CKVD overlay received VW < W " +
            $"(VW={vwMillimeters} mm, W={wMillimeters} mm). " +
            "VW case tolerance targets were skipped.");

        return OverlayVwCase.None;
    }

    private enum CkvdStyle
    {
        StyleA,
        StyleB
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
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

        var facts = new WedgeFacts(wedge);
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
        var style = ResolveStyle(facts);

        var hasOverlayVrFamily = HasAnyPositiveNominal(
            facts,
            "VR",
            "VRR",
            "VW");

        var overlayVwCase = ResolveOverlayVwCase(
            facts,
            hasOverlayVrFamily);

        if (subclass == WedgeSubclass.PGB)
        {
            AddPgbOverlayTolerances(
                updates,
                facts,
                style,
                hasOverlayVrFamily,
                overlayVwCase);
        }
        else
        {
            AddFgOverlayTolerances(
                updates,
                facts,
                style,
                hasOverlayVrFamily,
                overlayVwCase);
        }

        Logger.Info(
            "[CkvdToleranceRules] Overlay tolerance selection -> " +
            $"subclass={subclass}, style={style}, " +
            $"VR/VRR/VW present={hasOverlayVrFamily}, " +
            $"VW case={overlayVwCase}.");
    }

    private static void AddPgbOverlayTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        CkvdStyle style,
        bool hasOverlayVrFamily,
        OverlayVwCase overlayVwCase)
    {
        var styleFlSketch =
            style == CkvdStyle.StyleA
                ? "style_a_fl_pgb_overlay_sketch"
                : "style_b_fl_pgb_overlay_sketch";

        if (!hasOverlayVrFamily)
        {
            AddLengthMinimum(
                updates,
                facts,
                "W",
                "W_MIN@w_pgb_overlay_sketch");

            AddLengthMinimum(
                updates,
                facts,
                "FL",
                $"FL_MIN@{styleFlSketch}");

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
        bool hasOverlayVrFamily,
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

        if (!hasOverlayVrFamily)
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
        WedgeFacts facts)
    {
        var raw = facts.NormalizedPropertyToken(
            "Wed-Type",
            "Wed_Type",
            "Wed Type",
            "Shank_Type",
            "shank_type");

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
            "Unable to resolve the CKVD shank style from Wed-Type. " +
            "Expected 'LW_STYLE_A_CKVD' or 'LW_STYLE_B_CKVD', " +
            $"but received '{raw}'.");
    }

    private static bool HasAnyPositiveNominal(
        WedgeFacts facts,
        params string[] dimensionKeys)
    {
        foreach (var key in dimensionKeys)
        {
            if (facts.HasPositive(key))
                return true;
        }

        return false;
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasOverlayVrFamily)
    {
        if (!hasOverlayVrFamily ||
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
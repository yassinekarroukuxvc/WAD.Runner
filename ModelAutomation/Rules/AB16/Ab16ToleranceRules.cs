using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Equations;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.AB16;

/// <summary>
/// AB16 overlay tolerance rules.
/// Uses the exact AB16 overlay sketch/dimension names supplied by the model.
/// </summary>
public sealed class Ab16ToleranceRules : IToleranceRuleSet
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

        var facts = new WedgeFacts(wedge);
        var updates = new List<ToleranceUpdate>();

        AddOverlayCutReferencePointUpdates(updates, facts);

        AddSubclassWTolerances(updates, facts, subclass);
        AddVrCaseTolerances(updates, facts);
        AddTCaseTolerances(updates, facts);

        if (subclass == WedgeSubclass.FG)
            AddFgVgTolerances(updates, facts);

        Logger.Info(
            "[Ab16ToleranceRules] Planned -> " +
            $"updates={updates.Count}, subclass={subclass}, drawingType={drawingType}.");

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
        var magnification =
            EquationGeometry.OverlayMagnification(
                facts,
                WedgeType.AB16);

        var scale =
            EquationGeometry.OverlayScaleDecimal(
                magnification);

        // AB16 only uses the STD reference points already present in the model.
        // RIGHT keeps the existing calculated overlay cut.
        var rightCutMm =
            EquationGeometry.RefPointOverlayCutMm(
                facts,
                scale,
                WedgeType.AB16);

        // LEFT is always 1.5 inches converted to millimeters,
        // divided by the overlay scale.
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
            "[Ab16ToleranceRules] Overlay cut reference points -> " +
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
        WedgeSubclass subclass)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            AddLengthMinimum(
                updates,
                facts,
                "W",
                "W_MIN@w_pgb_overlay_sketch");

            return;
        }

        if (subclass != WedgeSubclass.FG)
            return;

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
    }

    // ================================================================
    // VR CASE TOLERANCES
    // ================================================================

    private static void AddVrCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        var hasVrVw =
            facts.HasPositive("VR") &&
            facts.HasPositive("VW");

        var vrCase =
            ResolveOverlayVrCase(
                facts,
                hasVrVw);

        switch (vrCase)
        {
            case OverlayVrCase.Case1:
                AddLengthMinimum(
                    updates,
                    facts,
                    "VW",
                    "VW_MIN@vr_case1_overlay_sketch");

                break;

            case OverlayVrCase.Case2:
                AddAngleMinimum(
                    updates,
                    facts,
                    "ISA",
                    "ISA_MIN@vr_case2_overlay_sketch");

                AddLengthMinimum(
                    updates,
                    facts,
                    "W",
                    "W_MIN@vr_case2_overlay_sketch");

                AddLengthMinimum(
                    updates,
                    facts,
                    "VW",
                    "VW_MIN@vr_case2_overlay_sketch");

                AddLengthMaximum(
                    updates,
                    facts,
                    "VR",
                    "VR_MAX@vr_case2_overlay_sketch");

                break;
        }
    }

    // ================================================================
    // FG VG TOLERANCES
    // ================================================================

    private static void AddFgVgTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        if (ResolveFootOption(facts) != FootOptionType.Vg)
            return;

        AddLengthMaximum(
            updates,
            facts,
            "B",
            "B_MAX@vg_overlay_sketch");

        AddLengthMaximum(
            updates,
            facts,
            "GD",
            "GD_MAX@vg_overlay_sketch");

        AddAngleMaximum(
            updates,
            facts,
            "GA",
            "GA_MAX@vg_overlay_sketch");
    }

    // ================================================================
    // T CASE TOLERANCES
    // ================================================================

    private static void AddTCaseTolerances(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        var tCase =
            ResolveOverlayTCase(
                facts.HasPositive("VBL"),
                facts.HasPositive("RA2"));

        var sketch =
            tCase switch
            {
                OverlayTCase.Case1 =>
                    "t_case1_overlay_sketch",

                OverlayTCase.Case2 =>
                    "t_case2_overlay_sketch",

                OverlayTCase.Case3 =>
                    "t_case3_overlay_sketch",

                OverlayTCase.Case4 =>
                    "t_case4_overlay_sketch",

                _ =>
                    throw new ArgumentOutOfRangeException()
            };

        AddLengthMinimum(
            updates,
            facts,
            "FD",
            $"FD_MIN@{sketch}");

        AddLengthMinimum(
            updates,
            facts,
            "T",
            $"T_MIN@{sketch}");

        if (tCase is
            OverlayTCase.Case3 or
            OverlayTCase.Case4)
        {
            AddLengthMinimum(
                updates,
                facts,
                "RA2H",
                $"RA2H_MIN@{sketch}");
        }
    }

    // ================================================================
    // VR CASE RESOLUTION
    // ================================================================

    private static OverlayVrCase ResolveOverlayVrCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return OverlayVrCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vwMm) ||
            !facts.TryGetLengthMm(
                "W",
                out var wMm))
        {
            return OverlayVrCase.None;
        }

        return decimal.Abs(
                   vwMm -
                   wMm) <=
               WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVrCase.Case1
            : OverlayVrCase.Case2;
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
    // FOOT OPTION RESOLUTION
    // ================================================================

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Foot_Option",
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Wed-Foot Option",
                "Foot_Option",
                "Foot Option",
                "foot_option");

        var token =
            NormalizePackedToken(raw);

        return token switch
        {
            "LW_VG" or "VG" =>
                FootOptionType.Vg,

            "LW_CG" or "CG" =>
                FootOptionType.Cg,

            _ =>
                FootOptionType.Unknown
        };
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
                out var minimumDeg,
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
                minimumDeg,
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
                out var maximumDeg))
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
                maximumDeg,
                ToleranceUnit.AngleDeg));
    }

    // ================================================================
    // LOGGING / TOKEN HELPERS
    // ================================================================

    private static void LogMissingBound(
        string dimensionKey,
        string target,
        string description)
    {
        Logger.Warn(
            "[Ab16ToleranceRules] Missing/invalid nominal or tolerance for " +
            $"'{dimensionKey}'. The {description} target " +
            $"'{target}' was skipped.");
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
            token = token[..separatorIndex];

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

    // ================================================================
    // ENUMS
    // ================================================================

    private enum FootOptionType
    {
        Unknown,
        Vg,
        Cg
    }

    private enum OverlayVrCase
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
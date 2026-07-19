using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.OSG7;

public sealed class Osg7ToleranceRules : IToleranceRuleSet
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

        if (subclass == WedgeSubclass.PGB)
        {
            BuildPgbOverlayRules(
                facts,
                updates);
        }
        else
        {
            BuildFgOverlayRules(
                facts,
                updates);
        }

        Logger.Info(
            $"[Osg7ToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}).");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    private static void BuildPgbOverlayRules(
        WedgeFacts facts,
        List<ToleranceUpdate> updates)
    {
        /*
         * Updated PGB FL targets:
         *
         * FL_MIN@PGB_FL_overlay_sketch
         * FL_MAX@PGB_FL_overlay_sketch
         *
         * These targets receive the complete
         * minimum and maximum FL values.
         */
        AddStandardBounds(
            updates,
            facts,
            dimensionKey: "FL",
            minTarget: "FL_MIN@PGB_FL_overlay_sketch",
            maxTarget: "FL_MAX@PGB_FL_overlay_sketch");

        /*
         * Updated PGB W targets:
         *
         * W_MIN@PGB_W_overlay_sketch
         * W_MAX@PGB_W_overlay_sketch
         *
         * These targets receive the complete
         * minimum and maximum W values.
         */
        AddStandardBounds(
            updates,
            facts,
            dimensionKey: "W",
            minTarget: "W_MIN@PGB_W_overlay_sketch",
            maxTarget: "W_MAX@PGB_W_overlay_sketch");
    }

    private static void BuildFgOverlayRules(
        WedgeFacts facts,
        List<ToleranceUpdate> updates)
    {
        /*
         * FG FL still uses tolerance-zone
         * displacement values:
         *
         * FL_LTOL@FG_FL_overlay_sketch
         * FL_UTOL@FG_FL_overlay_sketch
         *
         * Each target receives half of its
         * respective tolerance magnitude.
         */
        AddHalfTolerancePair(
            updates,
            facts,
            dimensionKey: "FL",
            upperTarget: "FL_UTOL@FG_FL_overlay_sketch",
            lowerTarget: "FL_LTOL@FG_FL_overlay_sketch");

        /*
         * Updated FG W targets:
         *
         * W_MIN@FG_W_overlay_sketch
         * W_MAX@FG_W_overlay_sketch
         */
        AddStandardBounds(
            updates,
            facts,
            dimensionKey: "W",
            minTarget: "W_MIN@FG_W_overlay_sketch",
            maxTarget: "W_MAX@FG_W_overlay_sketch");

        /*
         * FG_VR_overlay_sketch is only active when
         * the VR feature is present.
         */
        if (facts.HasPositive("VR"))
        {
            /*
             * Updated VW targets:
             *
             * VW_MIN@FG_VR_overlay_sketch
             * VW_MAX@FG_VR_overlay_sketch
             */
            AddStandardBounds(
                updates,
                facts,
                dimensionKey: "VW",
                minTarget: "VW_MIN@FG_VR_overlay_sketch",
                maxTarget: "VW_MAX@FG_VR_overlay_sketch");

            /*
             * VR targets:
             *
             * VR_MIN@FG_VR_overlay_sketch
             * VR_MAX@FG_VR_overlay_sketch
             */
            AddStandardBounds(
                updates,
                facts,
                dimensionKey: "VR",
                minTarget: "VR_MIN@FG_VR_overlay_sketch",
                maxTarget: "VR_MAX@FG_VR_overlay_sketch");

            /*
             * VRR targets:
             *
             * VRR_MIN@FG_VR_overlay_sketch
             * VRR_MAX@FG_VR_overlay_sketch
             *
             * The existing reversed mapping is
             * preserved because the direction of
             * the VRR sketch dimension is reversed.
             */
            AddReversedVrrBounds(
                updates,
                facts);
        }

        /*
         * FG_G_overlay_sketch is only active when
         * the G feature is present.
         *
         * The feature activation currently uses
         * a positive GD value.
         */
        if (facts.HasPositive("GD"))
        {
            /*
             * Updated B targets:
             *
             * B_MIN@FG_G_overlay_sketch
             * B_MAX@FG_G_overlay_sketch
             */
            AddStandardBounds(
                updates,
                facts,
                dimensionKey: "B",
                minTarget: "B_MIN@FG_G_overlay_sketch",
                maxTarget: "B_MAX@FG_G_overlay_sketch");

            /*
             * Updated GD targets:
             *
             * GD_MIN@FG_G_overlay_sketch
             * GD_MAX@FG_G_overlay_sketch
             */
            AddStandardBounds(
                updates,
                facts,
                dimensionKey: "GD",
                minTarget: "GD_MIN@FG_G_overlay_sketch",
                maxTarget: "GD_MAX@FG_G_overlay_sketch");

            /*
             * New GA targets:
             *
             * GA_MIN@FG_G_overlay_sketch
             * GA_MAX@FG_G_overlay_sketch
             */
            AddStandardBounds(
                updates,
                facts,
                dimensionKey: "GA",
                minTarget: "GA_MIN@FG_G_overlay_sketch",
                maxTarget: "GA_MAX@FG_G_overlay_sketch");
        }
    }

    /// <summary>
    /// Adds half of the upper and lower tolerance magnitudes.
    ///
    /// This is used by overlay sketch dimensions that represent
    /// the displacement of each side of the tolerance zone rather
    /// than the complete minimum and maximum dimension values.
    /// </summary>
    private static void AddHalfTolerancePair(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string upperTarget,
        string lowerTarget)
    {
        if (!facts.TryGetLengthToleranceMagnitudesMm(
                dimensionKey,
                out var lowerAbsoluteMm,
                out var upperAbsoluteMm))
        {
            Logger.Warn(
                "[Osg7ToleranceRules] " +
                $"Missing or invalid tolerance for '{dimensionKey}'. " +
                $"Targets '{upperTarget}' and '{lowerTarget}' were skipped.");

            return;
        }

        var upperHalfToleranceMm =
            upperAbsoluteMm / 2m;

        var lowerHalfToleranceMm =
            lowerAbsoluteMm / 2m;

        updates.Add(
            new ToleranceUpdate(
                upperTarget,
                upperHalfToleranceMm,
                ToleranceUnit.LengthMm));

        updates.Add(
            new ToleranceUpdate(
                lowerTarget,
                lowerHalfToleranceMm,
                ToleranceUnit.LengthMm));

        Logger.Info(
            "[Osg7ToleranceRules] Half-tolerance update -> " +
            $"dimension={dimensionKey}, " +
            $"lowerTarget={lowerTarget}, " +
            $"lowerValue={lowerHalfToleranceMm} mm, " +
            $"upperTarget={upperTarget}, " +
            $"upperValue={upperHalfToleranceMm} mm.");
    }

    /// <summary>
    /// Adds the complete mathematical minimum and maximum values:
    ///
    /// minimum = nominal - absolute lower tolerance
    /// maximum = nominal + absolute upper tolerance
    /// </summary>
    private static void AddStandardBounds(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string minTarget,
        string maxTarget)
    {
        if (!facts.TryGetLengthBoundsMm(
                dimensionKey,
                out var minimumMm,
                out var maximumMm))
        {
            Logger.Warn(
                "[Osg7ToleranceRules] " +
                $"Missing or invalid nominal/tolerance for '{dimensionKey}'. " +
                $"Targets '{minTarget}' and '{maxTarget}' were skipped.");

            return;
        }

        updates.Add(
            new ToleranceUpdate(
                minTarget,
                minimumMm,
                ToleranceUnit.LengthMm));

        updates.Add(
            new ToleranceUpdate(
                maxTarget,
                maximumMm,
                ToleranceUnit.LengthMm));

        Logger.Info(
            "[Osg7ToleranceRules] Bounds update -> " +
            $"dimension={dimensionKey}, " +
            $"minTarget={minTarget}, " +
            $"minimum={minimumMm} mm, " +
            $"maxTarget={maxTarget}, " +
            $"maximum={maximumMm} mm.");
    }

    /// <summary>
    /// Adds the VRR bounds using the reversed sketch direction.
    ///
    /// Mathematical minimum:
    /// nominal - lower tolerance
    ///
    /// Mathematical maximum:
    /// nominal + upper tolerance
    ///
    /// Because the SolidWorks sketch dimension runs in the
    /// opposite direction:
    ///
    /// VRR_MIN receives the mathematical maximum.
    /// VRR_MAX receives the mathematical minimum.
    /// </summary>
    private static void AddReversedVrrBounds(
        List<ToleranceUpdate> updates,
        WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm(
                "VRR",
                out var nominalMm) ||
            !facts.TryGetLengthToleranceMagnitudesMm(
                "VRR",
                out var lowerAbsoluteMm,
                out var upperAbsoluteMm))
        {
            Logger.Warn(
                "[Osg7ToleranceRules] " +
                "Missing or invalid VRR nominal/tolerance. " +
                "Targets 'VRR_MIN@FG_VR_overlay_sketch' and " +
                "'VRR_MAX@FG_VR_overlay_sketch' were skipped.");

            return;
        }

        var mathematicalMinimumMm =
            nominalMm - lowerAbsoluteMm;

        var mathematicalMaximumMm =
            nominalMm + upperAbsoluteMm;

        /*
         * Reversed target assignment:
         *
         * VRR_MIN target <- mathematical maximum
         * VRR_MAX target <- mathematical minimum
         */
        updates.Add(
            new ToleranceUpdate(
                "VRR_MIN@FG_VR_overlay_sketch",
                mathematicalMaximumMm,
                ToleranceUnit.LengthMm));

        updates.Add(
            new ToleranceUpdate(
                "VRR_MAX@FG_VR_overlay_sketch",
                mathematicalMinimumMm,
                ToleranceUnit.LengthMm));

        Logger.Info(
            "[Osg7ToleranceRules] Reversed VRR bounds update -> " +
            $"nominal={nominalMm} mm, " +
            $"VRR_MIN target value={mathematicalMaximumMm} mm, " +
            $"VRR_MAX target value={mathematicalMinimumMm} mm.");
    }
}
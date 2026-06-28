using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules.Common;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.OSG7;

public sealed class Osg7ToleranceRules : IToleranceRuleSet
{
    private const decimal PositiveEpsilonMm = 0.000001m;
    private const string LogPrefix = nameof(Osg7ToleranceRules);

    public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (drawingType != DrawingType.Overlay)
        {
            Logger.Info($"[Osg7ToleranceRules] {drawingType} drawing -> no overlay tolerance updates.");
            return TolerancePlan.Empty;
        }

        var updates = new List<ToleranceUpdate>();

        if (subclass == WedgeSubclass.PGB)
            BuildPgbOverlayRules(wedge, updates);
        else
            BuildFgOverlayRules(wedge, updates);

        Logger.Info($"[Osg7ToleranceRules] Planned updates={updates.Count} (Subclass={subclass}, DrawingType={drawingType})");

        return updates.Count == 0
            ? TolerancePlan.Empty
            : new TolerancePlan(updates);
    }

    private static void BuildPgbOverlayRules(WedgeData wedge, List<ToleranceUpdate> updates)
    {
        AddHalfTolPairMm(
            updates,
            wedge,
            dimKey: "FL",
            utolTarget: "FL_UTOL@PGB_FL_overlay_sketch",
            ltolTarget: "FL_LTOL@PGB_FL_overlay_sketch");

        AddHalfTolPairMm(
            updates,
            wedge,
            dimKey: "W",
            utolTarget: "W_UTOL@PGB_W_overlay_sketch",
            ltolTarget: "W_LTOL@PGB_W_overlay_sketch");
    }

    private static void BuildFgOverlayRules(WedgeData wedge, List<ToleranceUpdate> updates)
    {
        AddHalfTolPairMm(
            updates,
            wedge,
            dimKey: "W",
            utolTarget: "W_UTOL@FG_W_overlay_sketch",
            ltolTarget: "W_LTOL@FG_W_overlay_sketch");

        AddHalfTolPairMm(
            updates,
            wedge,
            dimKey: "FL",
            utolTarget: "FL_UTOL@FG_FL_overlay_sketch",
            ltolTarget: "FL_LTOL@FG_FL_overlay_sketch");

        if (HasPositiveNominalMm(wedge, "VR"))
        {
            AddVrBoundsMm(updates, wedge);
            AddVrrBoundsMm(updates, wedge);
        }
        else
        {
            Logger.Info("[Osg7ToleranceRules] VR <= 0 or missing -> skipping FG_VR_overlay_sketch tolerance updates.");
        }

        if (HasPositiveNominalMm(wedge, "GD"))
        {
            AddHalfTolPairMm(
                updates,
                wedge,
                dimKey: "GD",
                utolTarget: "GD_UTOL@FG_G_overlay_sketch",
                ltolTarget: "GD_LTOL@FG_G_overlay_sketch");
        }
        else
        {
            Logger.Info("[Osg7ToleranceRules] G <= 0 or missing -> skipping FG_G_overlay_sketch tolerance updates.");
        }
    }

    private static void AddTolPairMm(
        List<ToleranceUpdate> updates,
        WedgeData wedge,
        string dimKey,
        string utolTarget,
        string ltolTarget)
    {
        if (!WedgeDimensionReader.TryGetLengthToleranceMm(wedge, dimKey, out var lowerMm, out var upperMm, LogPrefix))
        {
            Logger.Warn($"[Osg7ToleranceRules] Missing/invalid tolerance for '{dimKey}' " +
                        $"(skipping: {ltolTarget}, {utolTarget}).");
            return;
        }

        var lAbs = decimal.Abs(lowerMm);
        var uAbs = decimal.Abs(upperMm);

        updates.Add(new ToleranceUpdate(utolTarget, uAbs, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(ltolTarget, lAbs, ToleranceUnit.LengthMm));

        Logger.Info($"[Osg7ToleranceRules] {dimKey}: UTOL={uAbs}mm -> {utolTarget}, LTOL={lAbs}mm -> {ltolTarget}");
    }

    private static void AddHalfTolPairMm(
        List<ToleranceUpdate> updates,
        WedgeData wedge,
        string dimKey,
        string utolTarget,
        string ltolTarget)
    {
        if (!WedgeDimensionReader.TryGetLengthToleranceMm(wedge, dimKey, out var lowerMm, out var upperMm, LogPrefix))
        {
            Logger.Warn($"[Osg7ToleranceRules] Missing/invalid tolerance for '{dimKey}' " +
                        $"(skipping half tolerance: {ltolTarget}, {utolTarget}).");
            return;
        }

        var lHalfAbs = decimal.Abs(lowerMm) / 2m;
        var uHalfAbs = decimal.Abs(upperMm) / 2m;

        updates.Add(new ToleranceUpdate(utolTarget, uHalfAbs, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(ltolTarget, lHalfAbs, ToleranceUnit.LengthMm));

        Logger.Info(
            $"[Osg7ToleranceRules] {dimKey}: HALF UTOL={uHalfAbs}mm -> {utolTarget}, " +
            $"HALF LTOL={lHalfAbs}mm -> {ltolTarget}");
    }

    private static void AddVrBoundsMm(List<ToleranceUpdate> updates, WedgeData wedge)
    {
        if (!TryGetLengthNominalAndToleranceMm(wedge, "VR", out var vrMm, out var lowerMm, out var upperMm))
        {
            Logger.Warn("[Osg7ToleranceRules] Missing/invalid VR nominal/tolerance (skipping VR bounds).");
            return;
        }

        var lAbs = decimal.Abs(lowerMm);
        var uAbs = decimal.Abs(upperMm);
        var vrMax = vrMm + uAbs;
        var vrMin = vrMm - lAbs;

        updates.Add(new ToleranceUpdate("VR_MAX@FG_VR_overlay_sketch", vrMax, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate("VR_MIN@FG_VR_overlay_sketch", vrMin, ToleranceUnit.LengthMm));

        Logger.Info($"[Osg7ToleranceRules] VR: NOM={vrMm}mm, UTOL={uAbs}mm, LTOL={lAbs}mm -> VR_MAX={vrMax}mm, VR_MIN={vrMin}mm");
    }

    private static void AddVrrBoundsMm(List<ToleranceUpdate> updates, WedgeData wedge)
    {
        if (!TryGetLengthNominalAndToleranceMm(wedge, "VRR", out var vrrMm, out var lowerMm, out var upperMm))
        {
            Logger.Warn("[Osg7ToleranceRules] Missing/invalid VRR nominal/tolerance (skipping VRR bounds).");
            return;
        }

        var lAbs = decimal.Abs(lowerMm);
        var uAbs = decimal.Abs(upperMm);

        var vrrMinTargetValue = vrrMm + uAbs;
        var vrrMaxTargetValue = vrrMm - lAbs;

        updates.Add(new ToleranceUpdate("VRR_MIN@FG_VR_overlay_sketch", vrrMinTargetValue, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate("VRR_MAX@FG_VR_overlay_sketch", vrrMaxTargetValue, ToleranceUnit.LengthMm));

        Logger.Info($"[Osg7ToleranceRules] VRR: NOM={vrrMm}mm, UTOL={uAbs}mm, LTOL={lAbs}mm -> VRR_MIN={vrrMinTargetValue}mm, VRR_MAX={vrrMaxTargetValue}mm");
    }

    private static bool HasPositiveNominalMm(WedgeData wedge, string dimKey)
    {
        return WedgeDimensionReader.TryGetLengthNominalMm(wedge, dimKey, out var valueMm, LogPrefix)
               && valueMm > PositiveEpsilonMm;
    }

    private static bool TryGetLengthNominalAndToleranceMm(
        WedgeData wedge,
        string dimKey,
        out decimal nominalMm,
        out decimal lowerMm,
        out decimal upperMm)
    {
        nominalMm = 0m;
        lowerMm = 0m;
        upperMm = 0m;

        return WedgeDimensionReader.TryGetLengthNominalMm(wedge, dimKey, out nominalMm, LogPrefix)
               && WedgeDimensionReader.TryGetLengthToleranceMm(wedge, dimKey, out lowerMm, out upperMm, LogPrefix);
    }
}

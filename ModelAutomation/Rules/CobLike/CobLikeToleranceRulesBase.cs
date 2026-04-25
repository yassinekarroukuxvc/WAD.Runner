using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared overlay tolerance planning for COB / FP / UTUS.
/// The concrete classes only provide the wedge-type log prefix.
/// </summary>
public abstract class CobLikeToleranceRulesBase : IToleranceRuleSet
{
    private readonly string _logPrefix;

    protected CobLikeToleranceRulesBase(string logPrefix)
    {
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "CobLikeToleranceRules" : logPrefix;
    }

    public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (drawingType != DrawingType.Overlay)
            return TolerancePlan.Empty;

        var facts = new CobLikeRuleFacts(wedge);
        var updates = new List<ToleranceUpdate>();

        var prefix = subclass == WedgeSubclass.FG ? "FG" : "PGB";
        bool isStd = facts.ShankType == CobLikeShankType.Std;
        string frontSketch = isStd ? "PGB_STD_FRONT_overlay_sketch" : "PGB_180_DEG_REV_FRONT_overlay_sketch";

        AddTolPairMm(updates, facts, dimKey: "W",
            utolTarget: $"W_UTOL@{prefix}_LEFT_overlay_sketch",
            ltolTarget: $"W_LTOL@{prefix}_LEFT_overlay_sketch");

        AddTolPairMm(updates, facts, dimKey: "FD",
            utolTarget: $"FD_UTOL@{frontSketch}",
            ltolTarget: $"FD_LTOL@{frontSketch}");

        AddTolPairMm(updates, facts, dimKey: "T",
            utolTarget: $"T_UTOL@{frontSketch}",
            ltolTarget: $"T_LTOL@{frontSketch}");

        if (facts.HasRa2H)
        {
            string ra2hSketch = isStd
                ? "RA2H_STD_FRONT_overlay_sketch"
                : "RA2H_180_DEG_REV_FRONT_overlay_sketch";

            AddTolPairMm(updates, facts, dimKey: "FL",
                utolTarget: $"FL_UTOL@{ra2hSketch}",
                ltolTarget: $"FL_LTOL@{ra2hSketch}");

            AddTolPairMm(updates, facts, dimKey: "RA2H",
                utolTarget: $"RA2H_UTOL@{ra2hSketch}",
                ltolTarget: $"RA2H_LTOL@{ra2hSketch}");

            AddTolPairMm(updates, facts, dimKey: "T",
                utolTarget: $"T_UTOL@{ra2hSketch}",
                ltolTarget: $"T_LTOL@{ra2hSketch}");

            Logger.Info($"[{_logPrefix}] RA2H > 0 → planned tolerances for {ra2hSketch}.");
        }

        if (facts.HasVbl)
        {
            string slbSketch = isStd
                ? "SLB_STD_overlay_sketch"
                : "SLB_180_DEG_REV_overlay_sketch";

            AddTolPairMm(updates, facts, dimKey: "FL",
                utolTarget: $"FL_UTOL@{slbSketch}",
                ltolTarget: $"FL_LTOL@{slbSketch}");

            AddTolPairMm(updates, facts, dimKey: "T",
                utolTarget: $"T_UTOL@{slbSketch}",
                ltolTarget: $"T_LTOL@{slbSketch}");

            Logger.Info($"[{_logPrefix}] SLB enabled (VBL > 0) → planned tolerances for {slbSketch}.");
        }

        if (facts.HasVr)
        {
            bool vwEqualsW = facts.AreNominalsEqual("VW", "W");

            if (vwEqualsW)
            {
                const string case2Sketch = "VW_LEFT_case_2_overlay_sketch";

                AddTolPairMm(updates, facts, dimKey: "VW",
                    utolTarget: $"VW_UTOL@{case2Sketch}",
                    ltolTarget: $"VW_LTOL@{case2Sketch}");

                AddComputedBoundsMm(updates, facts, nomKey: "VR",
                    maxTarget: $"VR_MAX@{case2Sketch}",
                    minTarget: $"VR_MIN@{case2Sketch}");

                AddComputedBoundsMm(updates, facts, nomKey: "VRR",
                    maxTarget: $"VRR_MAX@{case2Sketch}",
                    minTarget: $"VRR_MIN@{case2Sketch}");

                Logger.Info($"[{_logPrefix}] VW case 2 (VW == W) → planned VW, VR bounds, VRR bounds for {case2Sketch}.");
            }
            else
            {
                const string case1Sketch = "VW_LEFT_case_1_overlay_sketch";

                AddTolPairMm(updates, facts, dimKey: "W",
                    utolTarget: $"W_UTOL@{case1Sketch}",
                    ltolTarget: $"W_LTOL@{case1Sketch}");

                AddTolPairMm(updates, facts, dimKey: "VW",
                    utolTarget: $"VW_UTOL@{case1Sketch}",
                    ltolTarget: $"VW_LTOL@{case1Sketch}");

                AddComputedBoundsMm(updates, facts, nomKey: "VR",
                    maxTarget: $"VR_MAX@{case1Sketch}",
                    minTarget: $"VR_MIN@{case1Sketch}");

                AddComputedBoundsMm(updates, facts, nomKey: "VRR",
                    maxTarget: $"VRR_MAX@{case1Sketch}",
                    minTarget: $"VRR_MIN@{case1Sketch}");

                Logger.Info($"[{_logPrefix}] VW case 1 (VW != W) → planned W, VW, VR bounds, VRR bounds for {case1Sketch}.");
            }
        }

        Logger.Info($"[{_logPrefix}] {subclass} Overlay → planned updates={updates.Count} (Shank={facts.ShankType})");
        return updates.Count == 0 ? TolerancePlan.Empty : new TolerancePlan(updates);
    }

    private void AddTolPairMm(
        List<ToleranceUpdate> updates,
        CobLikeRuleFacts facts,
        string dimKey,
        string utolTarget,
        string ltolTarget)
    {
        if (!facts.TryGetLengthToleranceMm(dimKey, out var ltolMm, out var utolMm))
        {
            Logger.Warn($"[{_logPrefix}] Missing/invalid tolerance for '{dimKey}' (skipping: {ltolTarget}, {utolTarget})");
            return;
        }

        updates.Add(new ToleranceUpdate(utolTarget, utolMm, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(ltolTarget, ltolMm, ToleranceUnit.LengthMm));

        Logger.Info($"[{_logPrefix}] {dimKey}: LTOL={ltolMm}mm → {ltolTarget}, UTOL={utolMm}mm → {utolTarget}");
    }

    private void AddComputedBoundsMm(
        List<ToleranceUpdate> updates,
        CobLikeRuleFacts facts,
        string nomKey,
        string maxTarget,
        string minTarget)
    {
        if (!facts.TryGetLengthNominalMm(nomKey, out var nomMm))
        {
            Logger.Warn($"[{_logPrefix}] Missing/invalid nominal for '{nomKey}' (skipping: {maxTarget}, {minTarget}).");
            return;
        }

        if (!facts.TryGetLengthToleranceMm(nomKey, out var ltolMm, out var utolMm))
        {
            Logger.Warn($"[{_logPrefix}] Missing/invalid tolerance for '{nomKey}' (skipping: {maxTarget}, {minTarget}).");
            return;
        }

        var maxVal = nomMm + utolMm;
        var minVal = nomMm - ltolMm;

        updates.Add(new ToleranceUpdate(maxTarget, maxVal, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(minTarget, minVal, ToleranceUnit.LengthMm));

        Logger.Info($"[{_logPrefix}] {nomKey}: NOM={nomMm}, UTOL={utolMm}, LTOL={ltolMm} → MAX={maxVal} → {maxTarget}, MIN={minVal} → {minTarget}");
    }
}

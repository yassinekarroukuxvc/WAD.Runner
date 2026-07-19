using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

public abstract class CobLikeToleranceRulesBase : IToleranceRuleSet
{
    private readonly string _logPrefix;

    protected CobLikeToleranceRulesBase(string logPrefix)
    {
        _logPrefix = string.IsNullOrWhiteSpace(logPrefix)
            ? nameof(CobLikeToleranceRulesBase)
            : logPrefix;
    }

    public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (drawingType != DrawingType.Overlay) return TolerancePlan.Empty;

        var facts = new CobLikeFacts(wedge);
        var updates = new List<ToleranceUpdate>();
        var prefix = subclass == WedgeSubclass.FG ? "FG" : "PGB";
        var shank = facts.ShankType;
        var baseFrontSketch = CobLikeFeatureCatalog.FrontSketch(shank);

        AddHalfTolPairMm(
            updates,
            facts,
            "W",
            $"W_UTOL@{prefix}_LEFT_overlay_sketch",
            $"W_LTOL@{prefix}_LEFT_overlay_sketch");

        AddTolPairMm(
            updates,
            facts,
            "FD",
            $"FD_UTOL@{baseFrontSketch}",
            $"FD_LTOL@{baseFrontSketch}");

        AddTolPairMm(
            updates,
            facts,
            "T",
            $"T_UTOL@{baseFrontSketch}",
            $"T_LTOL@{baseFrontSketch}");

        AddSpecialFrontSketchTolerances(updates, facts, shank);
        AddLeftOverlayCaseTolerances(updates, facts);
        ApplySubclassSpecificTolerances(updates, facts, subclass);

        Logger.Info(
            $"[{_logPrefix}] {subclass} overlay -> planned updates={updates.Count} " +
            $"(Shank={facts.ShankType}, Foot={facts.FootOption})");

        return updates.Count == 0 ? TolerancePlan.Empty : new TolerancePlan(updates);
    }

    private void AddSpecialFrontSketchTolerances(
        List<ToleranceUpdate> updates,
        CobLikeFacts facts,
        CobLikeShankType shank)
    {
        if (!facts.HasRa2H && !facts.HasVbl)
            return;

        var activeSketch = CobLikeFeatureCatalog.ResolveFrontSketch(shank, facts.HasRa2H, facts.HasVbl);

        AddTolPairMm(
            updates,
            facts,
            "FL",
            $"FL_UTOL@{activeSketch}",
            $"FL_LTOL@{activeSketch}");

        AddTolPairMm(
            updates,
            facts,
            "T",
            $"T_UTOL@{activeSketch}",
            $"T_LTOL@{activeSketch}");

        if (facts.HasRa2H)
        {
            AddTolPairMm(
                updates,
                facts,
                "RA2H",
                $"RA2H_UTOL@{activeSketch}",
                $"RA2H_LTOL@{activeSketch}");
        }

        Logger.Info(
            $"[{_logPrefix}] Special front sketch tolerances -> RA2H={facts.HasRa2H}, " +
            $"VBL={facts.HasVbl}, sketch={activeSketch}.");
    }

    private void AddLeftOverlayCaseTolerances(List<ToleranceUpdate> updates, CobLikeFacts facts)
    {
        // Feature selection is driven by VW presence, so tolerance selection must use the same fact.
        if (!facts.HasVw)
            return;

        var vwEqualsW = facts.AreEqual("VW", "W");

        if (facts.HasLargeOverlayVrCase)
        {
            if (vwEqualsW)
            {
                AddHalfTolPairMm(
                    updates,
                    facts,
                    "VW",
                    $"VW_UTOL@{CobLikeFeatureCatalog.VwLeftCase4}",
                    $"VW_LTOL@{CobLikeFeatureCatalog.VwLeftCase4}");
            }
            else
            {
                AddHalfTolPairMm(
                    updates,
                    facts,
                    "W",
                    $"W_UTOL@{CobLikeFeatureCatalog.VwLeftCase3}",
                    $"W_LTOL@{CobLikeFeatureCatalog.VwLeftCase3}");
            }

            return;
        }

        if (vwEqualsW)
        {
            AddHalfTolPairMm(
                updates,
                facts,
                "VW",
                $"VW_UTOL@{CobLikeFeatureCatalog.VwLeftCase2}",
                $"VW_LTOL@{CobLikeFeatureCatalog.VwLeftCase2}");

            AddComputedBoundsMm(
                updates,
                facts,
                "VR",
                $"VR_MAX@{CobLikeFeatureCatalog.VwLeftCase2}",
                $"VR_MIN@{CobLikeFeatureCatalog.VwLeftCase2}");

            AddComputedBoundsMm(
                updates,
                facts,
                "VRR",
                $"VRR_MAX@{CobLikeFeatureCatalog.VwLeftCase2}",
                $"VRR_MIN@{CobLikeFeatureCatalog.VwLeftCase2}");

            return;
        }

        AddHalfTolPairMm(
            updates,
            facts,
            "W",
            $"W_UTOL@{CobLikeFeatureCatalog.VwLeftCase1}",
            $"W_LTOL@{CobLikeFeatureCatalog.VwLeftCase1}");

        AddHalfTolPairMm(
            updates,
            facts,
            "VW",
            $"VW_UTOL@{CobLikeFeatureCatalog.VwLeftCase1}",
            $"VW_LTOL@{CobLikeFeatureCatalog.VwLeftCase1}");

        AddComputedBoundsMm(
            updates,
            facts,
            "VR",
            $"VR_MAX@{CobLikeFeatureCatalog.VwLeftCase1}",
            $"VR_MIN@{CobLikeFeatureCatalog.VwLeftCase1}");

        AddComputedBoundsMm(
            updates,
            facts,
            "VRR",
            $"VRR_MAX@{CobLikeFeatureCatalog.VwLeftCase1}",
            $"VRR_MIN@{CobLikeFeatureCatalog.VwLeftCase1}");
    }

    protected string ResolveFootWidthSketch(CobLikeFacts facts)
        => CobLikeFeatureCatalog.FootWidthSketch(
            facts.FootOption,
            facts.NominalOrZero("W"),
            facts.NominalOrZero("VW"),
            facts.NominalOrZero("W2"));

    protected virtual void ApplySubclassSpecificTolerances(
        List<ToleranceUpdate> updates,
        CobLikeFacts facts,
        WedgeSubclass subclass)
    {
    }

    protected void AddTolPairMm(
        List<ToleranceUpdate> updates,
        CobLikeFacts facts,
        string dimKey,
        string utolTarget,
        string ltolTarget)
    {
        if (!facts.TryGetLengthToleranceMagnitudesMm(dimKey, out var lowerAbsMm, out var upperAbsMm))
        {
            Logger.Warn(
                $"[{_logPrefix}] Missing/invalid tolerance for '{dimKey}' " +
                $"(skipping: {ltolTarget}, {utolTarget}).");
            return;
        }

        updates.Add(new ToleranceUpdate(utolTarget, upperAbsMm, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(ltolTarget, lowerAbsMm, ToleranceUnit.LengthMm));
    }

    protected void AddHalfTolPairMm(
        List<ToleranceUpdate> updates,
        CobLikeFacts facts,
        string dimKey,
        string utolTarget,
        string ltolTarget)
    {
        if (!facts.TryGetLengthToleranceMagnitudesMm(dimKey, out var lowerAbsMm, out var upperAbsMm))
        {
            Logger.Warn(
                $"[{_logPrefix}] Missing/invalid tolerance for '{dimKey}' " +
                $"(skipping: {ltolTarget}, {utolTarget}).");
            return;
        }

        updates.Add(new ToleranceUpdate(utolTarget, upperAbsMm / 2m, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(ltolTarget, lowerAbsMm / 2m, ToleranceUnit.LengthMm));
    }

    protected void AddComputedBoundsMm(
        List<ToleranceUpdate> updates,
        CobLikeFacts facts,
        string nomKey,
        string maxTarget,
        string minTarget)
    {
        if (!facts.TryGetLengthBoundsMm(nomKey, out var minMm, out var maxMm))
        {
            Logger.Warn(
                $"[{_logPrefix}] Missing/invalid nominal or tolerance for '{nomKey}' " +
                $"(skipping: {maxTarget}, {minTarget}).");
            return;
        }

        updates.Add(new ToleranceUpdate(maxTarget, maxMm, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(minTarget, minMm, ToleranceUnit.LengthMm));
    }
}

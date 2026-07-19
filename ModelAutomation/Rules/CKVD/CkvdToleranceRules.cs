using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Tolerances;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

public sealed class CkvdToleranceRules : IToleranceRuleSet
{
    public TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));

        var facts = new WedgeFacts(wedge);
        var updates = new List<ToleranceUpdate>();

        AddBounds(
            updates,
            facts,
            "VR",
            "VR_MIN@FG_Wed_VW",
            "VR_MAX@FG_Wed_VW");

        foreach (var rule in GetToleranceSketchRules(subclass))
        {
            AddTolerancePair(
                updates,
                facts,
                rule.DimensionKey,
                rule.UpperTarget,
                rule.LowerTarget);
        }

        Logger.Info(
            $"[CkvdToleranceRules] Planned updates={updates.Count} " +
            $"(Subclass={subclass}, DrawingType={drawingType}).");

        return updates.Count == 0 ? TolerancePlan.Empty : new TolerancePlan(updates);
    }

    private static void AddTolerancePair(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string upperTarget,
        string lowerTarget)
    {
        if (!facts.TryGetLengthToleranceMagnitudesMm(dimensionKey, out var lowerAbsMm, out var upperAbsMm))
        {
            Logger.Warn($"[CkvdToleranceRules] Missing/invalid tolerance for '{dimensionKey}'.");
            return;
        }

        updates.Add(new ToleranceUpdate(upperTarget, upperAbsMm, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(lowerTarget, lowerAbsMm, ToleranceUnit.LengthMm));
    }

    private static void AddBounds(
        List<ToleranceUpdate> updates,
        WedgeFacts facts,
        string dimensionKey,
        string minTarget,
        string maxTarget)
    {
        if (!facts.TryGetLengthBoundsMm(dimensionKey, out var minMm, out var maxMm))
        {
            Logger.Warn($"[CkvdToleranceRules] Missing/invalid nominal or tolerance for '{dimensionKey}'.");
            return;
        }

        updates.Add(new ToleranceUpdate(minTarget, minMm, ToleranceUnit.LengthMm));
        updates.Add(new ToleranceUpdate(maxTarget, maxMm, ToleranceUnit.LengthMm));
    }

    private sealed record ToleranceSketchRule(
        string DimensionKey,
        string UpperTarget,
        string LowerTarget);

    private static IReadOnlyList<ToleranceSketchRule> GetToleranceSketchRules(WedgeSubclass subclass)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            return new[]
            {
                new ToleranceSketchRule("FL", "UTOL@PGB_Wed_FL", "LTOL@PGB_Wed_FL"),
                new ToleranceSketchRule("W", "UTOL@PGB_Wed_W", "LTOL@PGB_Wed_W")
            };
        }

        return new[]
        {
            new ToleranceSketchRule("FL", "UTOL@FG_Wed_FL", "LTOL@FG_Wed_FL"),
            new ToleranceSketchRule("W", "UTOL@FG_Wed_W", "LTOL@FG_Wed_W"),
            new ToleranceSketchRule("B", "UTOL@FG_Wed_B", "LTOL@FG_Wed_B"),
            new ToleranceSketchRule("VW", "VW_UTOL@FG_Wed_VW", "VW_LTOL@FG_Wed_VW"),
            new ToleranceSketchRule("VR", "UTOL@FG_Wed_VR", "LTOL@FG_Wed_VR")
        };
    }
}

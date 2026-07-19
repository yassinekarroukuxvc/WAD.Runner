using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

public sealed class CkvdFeatureRules : IFeatureRuleSet
{
    private const decimal EqualityToleranceMm = WedgeFacts.DefaultPositiveEpsilon;

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var facts = new WedgeFacts(wedge);
        var suppress = new List<string>();
        var unsuppress = new List<string>();

        Logger.Info($"[CkvdFeatureRules] Build -> subclass={context.Subclass}, drawingType={context.DrawingType}");

        suppress.Add(SwNames.EngravingFeature);
        suppress.Add(SwNames.EngravingSketch);

        if (context.Subclass != WedgeSubclass.FG)
        {
            Logger.Info("[CkvdFeatureRules] Non-FG -> skip FG-only TIP/VW-W rules.");
            return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
        }

        AddTipGuardPlan(facts, suppress, unsuppress);
        AddOverlayVwWTogglePlan(facts, context.DrawingType == DrawingType.Overlay, suppress, unsuppress);

        return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
    }

    private static void AddTipGuardPlan(
        WedgeFacts facts,
        List<string> suppress,
        List<string> unsuppress)
    {
        if (!facts.TryGetLengthMm("TIP", out var tipMm))
        {
            Logger.Info("[CkvdFeatureRules] TIP not present/mm -> TIP guard skipped.");
            return;
        }

        if (tipMm <= WedgeFacts.DefaultPositiveEpsilon)
        {
            suppress.Add(SwNames.SketchCrmet);
            Logger.Info($"[CkvdFeatureRules] TIP={tipMm} mm -> suppress '{SwNames.SketchCrmet}'.");
        }
        else
        {
            unsuppress.Add(SwNames.SketchCrmet);
            Logger.Info($"[CkvdFeatureRules] TIP={tipMm} mm -> unsuppress '{SwNames.SketchCrmet}'.");
        }
    }

    private static void AddOverlayVwWTogglePlan(
        WedgeFacts facts,
        bool isOverlay,
        List<string> suppress,
        List<string> unsuppress)
    {
        if (!isOverlay)
            return;

        var hasVw = facts.TryGetLengthMm("VW", out var vwMm);
        var hasW = facts.TryGetLengthMm("W", out var wMm);

        if (!(hasVw && hasW))
        {
            Logger.Warn("[CkvdFeatureRules] Missing VW or W -> defaulting to the W sketch.");
            unsuppress.Add(SwNames.SketchFgWedW);
            suppress.Add(SwNames.SketchFgWedVW);
            return;
        }

        var equal = decimal.Abs(vwMm - wMm) <= EqualityToleranceMm;
        if (equal)
        {
            unsuppress.Add(SwNames.SketchFgWedVW);
            suppress.Add(SwNames.SketchFgWedW);
        }
        else
        {
            unsuppress.Add(SwNames.SketchFgWedW);
            suppress.Add(SwNames.SketchFgWedVW);
        }

        Logger.Info($"[CkvdFeatureRules] VW={vwMm} mm, W={wMm} mm, equal={equal}.");
    }
}

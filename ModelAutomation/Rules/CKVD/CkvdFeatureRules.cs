using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

/// <summary>
/// CKVD feature-toggle planning.
/// This class is pure planning only: it does not call SolidWorks and does not rebuild.
/// </summary>
public sealed class CkvdFeatureRules : IFeatureRuleSet
{
    private const decimal EqualityToleranceMm = 0.000001m;

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var suppress = new List<string>();
        var unsuppress = new List<string>();

        Logger.Info($"[CkvdFeatureRules] Build -> subclass={context.Subclass}, drawingType={context.DrawingType}");

        AddEngravingSuppression(suppress);

        if (context.Subclass != WedgeSubclass.FG)
        {
            Logger.Info("[CkvdFeatureRules] Non-FG -> skip FG-only TIP/VW-W rules.");
            return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
        }

        AddTipGuardPlan(wedge, suppress, unsuppress);
        AddOverlayVwWTogglePlan(wedge, context.DrawingType == DrawingType.Overlay, suppress, unsuppress);

        return new ModelRuleRunner.FeaturePlan(suppress, unsuppress);
    }

    private static void AddEngravingSuppression(List<string> suppress)
    {
        suppress.Add(SwNames.EngravingFeature);
        suppress.Add(SwNames.EngravingSketch);

        Logger.Info(
            $"[CkvdFeatureRules] Always suppress engraving -> feature='{SwNames.EngravingFeature}', sketch='{SwNames.EngravingSketch}'");
    }

    private static void AddTipGuardPlan(WedgeData wedge, List<string> suppress, List<string> unsuppress)
    {
        if (!WedgeDimensionReader.TryGetLengthNominalMm(wedge, "TIP", out var tipMm, nameof(CkvdFeatureRules)))
        {
            Logger.Blue("[CkvdFeatureRules] TIP not present/mm -> TIP guard skipped.");
            return;
        }

        if (tipMm == 0m)
        {
            suppress.Add(SwNames.SketchCrmet);
            Logger.Info($"[CkvdFeatureRules] TIP={tipMm} mm -> suppress '{SwNames.SketchCrmet}'");
        }
        else
        {
            unsuppress.Add(SwNames.SketchCrmet);
            Logger.Info($"[CkvdFeatureRules] TIP={tipMm} mm -> unsuppress '{SwNames.SketchCrmet}'");
        }
    }

    private static void AddOverlayVwWTogglePlan(
        WedgeData wedge,
        bool isOverlay,
        List<string> suppress,
        List<string> unsuppress)
    {
        Logger.Info($"[CkvdFeatureRules] AddOverlayVwWTogglePlan -> overlay={isOverlay}");

        if (!isOverlay)
        {
            Logger.Blue("[CkvdFeatureRules] Not Overlay -> skip VW/W toggle.");
            return;
        }

        var hasVw = WedgeDimensionReader.TryGetLengthNominalMm(wedge, "VW", out var vwMm, nameof(CkvdFeatureRules));
        var hasW = WedgeDimensionReader.TryGetLengthNominalMm(wedge, "W", out var wMm, nameof(CkvdFeatureRules));

        if (!(hasVw && hasW))
        {
            Logger.Warn("[CkvdFeatureRules] Missing VW or W (or not mm) -> default to W sketch enabled.");
            unsuppress.Add(SwNames.SketchFgWedW);
            suppress.Add(SwNames.SketchFgWedVW);
            return;
        }

        var equal = Math.Abs(vwMm - wMm) <= EqualityToleranceMm;
        Logger.Info($"[CkvdFeatureRules] VW={vwMm} mm, W={wMm} mm, equal≈{equal}");

        if (equal)
        {
            Logger.Info("[CkvdFeatureRules] VW≈W -> enable VW sketch, disable W sketch.");
            unsuppress.Add(SwNames.SketchFgWedVW);
            suppress.Add(SwNames.SketchFgWedW);
        }
        else
        {
            Logger.Info("[CkvdFeatureRules] VW≠W -> enable W sketch, disable VW sketch.");
            unsuppress.Add(SwNames.SketchFgWedW);
            suppress.Add(SwNames.SketchFgWedVW);
        }
    }
}

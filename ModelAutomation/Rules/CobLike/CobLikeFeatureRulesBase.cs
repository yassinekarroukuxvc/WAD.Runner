using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Declarative COB-like feature planner.
/// The workflow is: known catalog -> active logical groups -> variant adjustments -> force suppress.
/// </summary>
public abstract class CobLikeFeatureRulesBase : IFeatureRuleSet
{
    protected abstract string LogPrefix { get; }

    protected virtual void ApplyVariantAdjustments(
        CobLikeFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context,
        FeaturePlanBuilder plan)
    {
    }

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var facts = new CobLikeFacts(wedge);
        var shank = facts.ShankType;
        var plan = new FeaturePlanBuilder().Know(CobLikeFeatureCatalog.AllManagedNames()).Activate(CobLikeFeatureCatalog.GlobalCore());

        Logger.Info($"[{LogPrefix}] Build. subclass={context.Subclass}, drawingType={context.DrawingType}, shank={shank}");

        if (context.Subclass == WedgeSubclass.PGB)
            AddPgbPlan(plan, facts, shank, context);
        else
            AddFgPlan(plan, facts, shank, context);

        ApplyVariantAdjustments(facts, shank, context, plan);
        ApplyOverlayCutProfile(context, plan);
        return plan.Build();
    }

    protected static void AddFeatureGroup(FeaturePlanBuilder plan, string baseName, CobLikeShankType shank)
        => plan.Activate(CobLikeFeatureCatalog.Group(shank, baseName));

    protected static void ForceSuppressFeatureGroup(FeaturePlanBuilder plan, string baseName, CobLikeShankType shank)
        => plan.ForceSuppress(CobLikeFeatureCatalog.Group(shank, baseName));

    private void AddPgbPlan(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank, FeatureRuleContext context)
    {
        plan.Activate(CobLikeFeatureCatalog.PgbCore(shank));

        When(facts.IsPositive("VW"), () => AddFeatureGroup(plan, "VW", shank));
        When(facts.IsPositive("RA2"), () => AddFeatureGroup(plan, "RA2", shank));
        plan.ForceSuppress(CobLikeFeatureCatalog.PgbForcedSuppressions());

        if (context.DrawingType == DrawingType.Overlay)
            AddPgbOverlay(plan, facts, shank);
    }

    private void AddFgPlan(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank, FeatureRuleContext context)
    {
        plan.Activate(CobLikeFeatureCatalog.FgCore(shank));
        When(facts.IsPositive("FRO"), () => AddFeatureGroup(plan, "FRO", shank));
        When(facts.IsPositive("VBL"), () => AddFeatureGroup(plan, "SLB", shank));
        When(facts.IsPositive("VW"), () => AddFeatureGroup(plan, "VW", shank));
        When(facts.IsPositive("W2"), () => AddFeatureGroup(plan, "W2", shank));
        When(facts.IsPositive("RA2"), () => AddFeatureGroup(plan, "RA2", shank));
        AddFootOption(plan, facts.ResolveFootOption(), shank);

        if (context.DrawingType == DrawingType.Overlay)
            AddFgOverlay(plan, facts, shank);
    }

    private static void AddPgbOverlay(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank)
    {
        plan.Activate(CobLikeFeatureCatalog.OverlayCommon());
        plan.Activate(CobLikeFeatureCatalog.FrontOverlayFeature(shank));
        plan.Activate(facts.HasVr ? CobLikeFeatureCatalog.OverlayCutNonStandard() : CobLikeFeatureCatalog.OverlayCutStandard());
        plan.Activate(ResolveLeftOverlaySketch(facts, pgb: true));
        AddFrontOverlaySketch(plan, facts, shank);
    }

    private static void AddFgOverlay(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank)
    {
        plan.Activate(CobLikeFeatureCatalog.OverlayCommon());
        plan.Activate(facts.HasVr ? CobLikeFeatureCatalog.OverlayCutNonStandard() : new[] { "cut_feature" });
        plan.Activate(ResolveLeftOverlaySketch(facts, pgb: false));
        AddFrontOverlaySketch(plan, facts, shank);
    }

    private static void AddFrontOverlaySketch(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank)
    {
        if (facts.HasRa2H)
            plan.Activate(CobLikeFeatureCatalog.Ra2HOverlaySketch(shank));
        else
            plan.Activate(CobLikeFeatureCatalog.FrontOverlaySketch(shank));

        if (facts.HasVbl)
        {
            plan.Activate(CobLikeFeatureCatalog.SlbOverlaySketch(shank));
            plan.Deactivate(CobLikeFeatureCatalog.FrontOverlaySketch(shank));
        }
    }

    private static string ResolveLeftOverlaySketch(CobLikeFacts facts, bool pgb)
    {
        if (!facts.HasVw) return pgb ? "PGB_LEFT_overlay_sketch" : "FG_LEFT_overlay_sketch";
        if (facts.HasLargeOverlayVrCase) return facts.AreEqual("VW", "W") ? "VW_LEFT_case_4_overlay_sketch" : "VW_LEFT_case_3_overlay_sketch";
        return facts.AreEqual("VW", "W") ? "VW_LEFT_case_2_overlay_sketch" : "VW_LEFT_case_1_overlay_sketch";
    }

    private static void AddFootOption(FeaturePlanBuilder plan, CobLikeFootOption foot, CobLikeShankType shank)
    {
        switch (foot)
        {
            case CobLikeFootOption.C:
                plan.Activate(CobLikeFeatureCatalog.Groups(shank, "C", "BR_C", "FR_C"));
                break;
            case CobLikeFootOption.G:
                plan.Activate(CobLikeFeatureCatalog.Groups(shank, "G", "BR_G", "FR_G"));
                break;
            case CobLikeFootOption.VG:
                plan.Activate(CobLikeFeatureCatalog.Groups(shank, "VG", "BR_VG", "FR_VG"));
                break;
            case CobLikeFootOption.CC:
                plan.Activate(CobLikeFeatureCatalog.Groups(shank, "C", "CG", "BR_C", "FR_C"));
                break;
            case CobLikeFootOption.C_WithCbr:
                plan.Activate(CobLikeFeatureCatalog.Groups(shank, "C", "CBRA", "FR_C"));
                break;
        }
    }

    private void ApplyOverlayCutProfile(FeatureRuleContext context, FeaturePlanBuilder plan)
    {
        if (context.DrawingType != DrawingType.Overlay) return;
        var profile = ResolveOverlayCutProfile(context);
        if (profile is null) return;

        if (profile is "default_config" or "std_cut")
        {
            plan.Activate(CobLikeFeatureCatalog.OverlayCutStandard());
            plan.ForceSuppress(CobLikeFeatureCatalog.OverlayCutNonStandard());
            Logger.Info($"[{LogPrefix}] Overlay profile '{profile}' -> standard cut.");
        }
        else if (profile == "non_std_cut")
        {
            plan.Activate(CobLikeFeatureCatalog.OverlayCutNonStandard());
            plan.ForceSuppress(CobLikeFeatureCatalog.OverlayCutStandard());
            Logger.Info($"[{LogPrefix}] Overlay profile 'non_std_cut' -> non-standard cut.");
        }
    }

    private static string? ResolveOverlayCutProfile(FeatureRuleContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.FeatureRuleProfile)) return context.FeatureRuleProfile.Trim();
        if (string.Equals(context.TargetConfigurationName, "non_std_cut", StringComparison.OrdinalIgnoreCase)) return "non_std_cut";
        if (string.Equals(context.TargetConfigurationName, "std_cut", StringComparison.OrdinalIgnoreCase)) return "std_cut";
        if (string.Equals(context.TargetConfigurationName, "Default", StringComparison.OrdinalIgnoreCase)) return "default_config";
        return null;
    }

    private static void When(bool condition, Action action)
    {
        if (condition) action();
    }
}

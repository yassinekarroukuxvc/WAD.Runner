using System;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Shared feature rules for the COB family. Derived wedge types only need to add or suppress
/// their small variant-specific feature set in <see cref="ApplyVariantAdjustments"/>.
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
        var foot = facts.FootOption;

        Logger.Info(
            $"[{LogPrefix}] Build — subclass={context.Subclass}, drawing={context.DrawingType}, " +
            $"shank={shank}, foot={foot}, ruleProfile={context.FeatureRuleProfile ?? "(none)"}");

        var plan = new FeaturePlanBuilder()
            .Know(CobLikeFeatureCatalog.AllManagedNames())
            .Activate(CobLikeFeatureCatalog.GlobalCore());

        if (context.Subclass == WedgeSubclass.PGB)
            BuildPgbPlan(plan, facts, shank, context);
        else
            BuildFgPlan(plan, facts, shank, foot, context);

        ApplyVariantAdjustments(facts, shank, context, plan);
        ApplyOverlayCutProfileOverride(context, plan);

        return plan.Build();
    }

    private static void BuildPgbPlan(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context)
    {
        plan.Activate(CobLikeFeatureCatalog.PgbCore(shank));

        if (facts.HasVw)
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("VW", shank));
        if (facts.IsPositive("RA2"))
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("RA2", shank));

        // ERW is never used by PGB, regardless of shank or configuration.
        plan.ForceSuppress(CobLikeFeatureCatalog.PgbErwSuppressions());

        if (context.DrawingType != DrawingType.Overlay)
            return;

        plan.Activate(CobLikeFeatureCatalog.OverlayCommon());
        plan.Activate(CobLikeFeatureCatalog.PgbFrontOverlayFeature(shank));
        ActivateOverlayCut(plan, facts.HasVr);
        ActivateLeftSketch(plan, facts, pgb: true);
        ActivateFrontSketch(plan, facts, shank);
    }

    private static void BuildFgPlan(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeShankType shank,
        CobLikeFootOption foot,
        FeatureRuleContext context)
    {
        plan.Activate(CobLikeFeatureCatalog.FgCore(shank));

        if (facts.IsPositive("FRO"))
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("FRO", shank));
        if (facts.HasVbl)
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("SLB", shank));
        if (facts.HasVw)
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("VW", shank));
        if (facts.IsPositive("W2"))
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("W2", shank));
        if (facts.IsPositive("RA2"))
            plan.Activate(CobLikeFeatureCatalog.FeatureNames("RA2", shank));

        plan.Activate(CobLikeFeatureCatalog.FootFeatures(foot, shank));

        if (context.DrawingType != DrawingType.Overlay)
            return;

        plan.Activate(CobLikeFeatureCatalog.OverlayCommon());
        ActivateOverlayCut(plan, facts.HasVr);
        ActivateFrontSketch(plan, facts, shank);
        ActivateErwOverlaySketch(plan, shank);
        ActivateFootWidthSketch(plan, facts, foot);
        ActivateLeftSketch(plan, facts, pgb: false);
    }

    private static void ActivateOverlayCut(FeaturePlanBuilder plan, bool useNonStandardCut)
    {
        // These two groups are mutually exclusive. Use normal deactivation rather than
        // ForceSuppress so a later explicit configuration profile can safely override the choice.
        plan.Deactivate(CobLikeFeatureCatalog.OverlayCutStandard());
        plan.Deactivate(CobLikeFeatureCatalog.OverlayCutNonStandard());
        plan.Activate(useNonStandardCut
            ? CobLikeFeatureCatalog.OverlayCutNonStandard()
            : CobLikeFeatureCatalog.OverlayCutStandard());
    }

    private static void ActivateFrontSketch(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeShankType shank)
    {
        var active = CobLikeFeatureCatalog.ResolveFrontSketch(shank, facts.HasRa2H, facts.HasVbl);
        plan.ActivateOnly(active, CobLikeFeatureCatalog.AllFrontSketches(shank));

        Logger.Info(
            $"[CobLikeFeatureRules] Front overlay sketch — RA2H={facts.HasRa2H}, " +
            $"VBL={facts.HasVbl}, active={active}");
    }

    private static void ActivateLeftSketch(FeaturePlanBuilder plan, CobLikeFacts facts, bool pgb)
    {
        var active = ResolveLeftSketch(facts, pgb);
        plan.ActivateOnly(active, CobLikeFeatureCatalog.AllLeftOverlaySketches());
        Logger.Info($"[CobLikeFeatureRules] Left overlay sketch — active={active}");
    }

    private static string ResolveLeftSketch(CobLikeFacts facts, bool pgb)
    {
        if (!facts.HasVw)
            return pgb ? CobLikeFeatureCatalog.LeftSketchPgb : CobLikeFeatureCatalog.LeftSketchFg;

        if (facts.HasLargeOverlayVrCase)
        {
            return facts.AreEqual("VW", "W")
                ? CobLikeFeatureCatalog.VwLeftCase4
                : CobLikeFeatureCatalog.VwLeftCase3;
        }

        return facts.AreEqual("VW", "W")
            ? CobLikeFeatureCatalog.VwLeftCase2
            : CobLikeFeatureCatalog.VwLeftCase1;
    }

    private static void ActivateErwOverlaySketch(FeaturePlanBuilder plan, CobLikeShankType shank)
    {
        var active = CobLikeFeatureCatalog.ErwOverlaySketch(shank);
        plan.ActivateOnly(active, CobLikeFeatureCatalog.AllErwOverlaySketches());
    }

    private static void ActivateFootWidthSketch(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeFootOption foot)
    {
        var active = CobLikeFeatureCatalog.FootWidthSketch(
            foot,
            facts.NominalOrZero("W"),
            facts.NominalOrZero("VW"),
            facts.NominalOrZero("W2"));

        plan.ActivateOnly(active, CobLikeFeatureCatalog.AllFootWidthSketches());
        Logger.Info($"[CobLikeFeatureRules] Foot-width overlay sketch — active={active}");
    }

    private void ApplyOverlayCutProfileOverride(FeatureRuleContext context, FeaturePlanBuilder plan)
    {
        if (context.DrawingType != DrawingType.Overlay)
            return;

        var profile = ResolveOverlayCutProfile(context);
        if (profile is null)
            return;

        if (profile is OverlayCutProfiles.DefaultConfiguration or OverlayCutProfiles.StandardCut)
        {
            ActivateOverlayCut(plan, useNonStandardCut: false);
            Logger.Info($"[{LogPrefix}] Overlay profile '{profile}' -> standard cut.");
        }
        else if (profile == OverlayCutProfiles.NonStandardCut)
        {
            ActivateOverlayCut(plan, useNonStandardCut: true);
            Logger.Info($"[{LogPrefix}] Overlay profile '{profile}' -> non-standard cut.");
        }
        else
        {
            Logger.Warn($"[{LogPrefix}] Unknown overlay feature-rule profile '{profile}'. Wedge facts remain authoritative.");
        }
    }

    private static string? ResolveOverlayCutProfile(FeatureRuleContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.FeatureRuleProfile))
            return context.FeatureRuleProfile.Trim().ToLowerInvariant();

        return context.TargetConfigurationName?.Trim().ToLowerInvariant() switch
        {
            "non_std_cut" => OverlayCutProfiles.NonStandardCut,
            "std_cut" => OverlayCutProfiles.StandardCut,
            "default" => OverlayCutProfiles.DefaultConfiguration,
            _ => null
        };
    }

    protected static void ActivateFeature(FeaturePlanBuilder plan, string baseName, CobLikeShankType shank)
        => plan.Activate(CobLikeFeatureCatalog.FeatureNames(baseName, shank));

    protected static void SuppressFeature(FeaturePlanBuilder plan, string baseName, CobLikeShankType shank)
        => plan.ForceSuppress(CobLikeFeatureCatalog.FeatureNames(baseName, shank));
}

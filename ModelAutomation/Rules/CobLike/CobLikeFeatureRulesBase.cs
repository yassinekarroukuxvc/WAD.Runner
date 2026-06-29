using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.CobLike;

/// <summary>
/// Base class for COB-like feature rule sets.
///
/// Build() is the single entry point. It:
///   1. Resolves facts (shank type, foot option, optional dimensions).
///   2. Activates the correct feature group for the drawing subclass (PGB / FG).
///   3. For overlays, activates the correct overlay sketches and suppresses the rest.
///   4. Calls ApplyVariantAdjustments() so derived classes can layer on type-specific tweaks.
///   5. Applies the overlay cut profile override if one is in the context.
/// </summary>
public abstract class CobLikeFeatureRulesBase : IFeatureRuleSet
{
    protected abstract string LogPrefix { get; }

    /// <summary>
    /// Override in derived classes to add type-specific feature activations
    /// (e.g. CobFeatureRules enabling COB-only features).
    /// </summary>
    protected virtual void ApplyVariantAdjustments(
        CobLikeFacts facts,
        CobLikeShankType shank,
        FeatureRuleContext context,
        FeaturePlanBuilder plan)
    { }

    // =========================================================================
    // Entry point
    // =========================================================================

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var facts = new CobLikeFacts(wedge);
        var shank = facts.ShankType;
        var foot = facts.ResolveFootOption();

        Logger.Info($"[{LogPrefix}] Build — subclass={context.Subclass}, drawing={context.DrawingType}, shank={shank}, foot={foot}");

        var plan = new FeaturePlanBuilder()
            .Know(CobLikeFeatureCatalog.AllManagedNames())
            .Activate(CobLikeFeatureCatalog.GlobalCore());

        if (context.Subclass == WedgeSubclass.PGB)
            BuildPgbPlan(plan, facts, shank, foot, context);
        else
            BuildFgPlan(plan, facts, shank, foot, context);

        ApplyVariantAdjustments(facts, shank, context, plan);
        ApplyOverlayCutProfileOverride(context, plan);

        return plan.Build();
    }

    // =========================================================================
    // PGB plan
    // =========================================================================

    private void BuildPgbPlan(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeShankType shank,
        CobLikeFootOption foot,
        FeatureRuleContext context)
    {
        // Core features
        plan.Activate(CobLikeFeatureCatalog.PgbCore(shank));

        // Optional features
        if (facts.IsPositive("VW")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("VW", shank));
        if (facts.IsPositive("RA2")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("RA2", shank));

        // ERW is never used for PGB
        plan.ForceSuppress(CobLikeFeatureCatalog.PgbErwSuppressions());

        if (context.DrawingType == DrawingType.Overlay)
            BuildPgbOverlay(plan, facts, shank);
    }

    private static void BuildPgbOverlay(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank)
    {
        plan.Activate(CobLikeFeatureCatalog.OverlayCommon());
        plan.Activate(CobLikeFeatureCatalog.PgbFrontOverlayFeature(shank));
        plan.Activate(facts.HasVr
            ? CobLikeFeatureCatalog.OverlayCutNonStandard()
            : CobLikeFeatureCatalog.OverlayCutStandard());

        plan.Activate(ResolveLeftSketch(facts, pgb: true));
        ActivateFrontSketch(plan, facts, shank);
    }

    // =========================================================================
    // FG plan
    // =========================================================================

    private void BuildFgPlan(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeShankType shank,
        CobLikeFootOption foot,
        FeatureRuleContext context)
    {
        // Core features
        plan.Activate(CobLikeFeatureCatalog.FgCore(shank));

        // Optional features
        if (facts.IsPositive("FRO")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("FRO", shank));
        if (facts.IsPositive("VBL")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("SLB", shank));
        if (facts.IsPositive("VW")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("VW", shank));
        if (facts.IsPositive("W2")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("W2", shank));
        if (facts.IsPositive("RA2")) plan.Activate(CobLikeFeatureCatalog.FeatureNames("RA2", shank));

        // Foot option features
        plan.Activate(CobLikeFeatureCatalog.FootFeatures(foot, shank));

        if (context.DrawingType == DrawingType.Overlay)
            BuildFgOverlay(plan, facts, shank, foot);
    }

    private static void BuildFgOverlay(
        FeaturePlanBuilder plan,
        CobLikeFacts facts,
        CobLikeShankType shank,
        CobLikeFootOption foot)
    {
        plan.Activate(CobLikeFeatureCatalog.OverlayCommon());
        plan.Activate(facts.HasVr
            ? CobLikeFeatureCatalog.OverlayCutNonStandard()
            : new[] { "cut_feature" });

        plan.Activate(ResolveLeftSketch(facts, pgb: false));
        ActivateFrontSketch(plan, facts, shank);
        ActivateErwOverlaySketch(plan, shank);
        ActivateFootWidthSketch(plan, facts, foot);
    }

    // =========================================================================
    // Overlay sketch helpers
    // =========================================================================

    /// <summary>
    /// Activates exactly one front overlay sketch (front / RA2H / SLB / RA2H+SLB)
    /// and deactivates the other three for the given shank.
    /// </summary>
    private static void ActivateFrontSketch(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeShankType shank)
    {
        var front = CobLikeFeatureCatalog.FrontSketch(shank);
        var ra2h = CobLikeFeatureCatalog.Ra2HSketch(shank);
        var slb = CobLikeFeatureCatalog.SlbSketch(shank);
        var ra2hSlb = CobLikeFeatureCatalog.Ra2HSlbSketch(shank);

        var active = (facts.HasRa2H, facts.HasVbl) switch
        {
            (true, true) => ra2hSlb,
            (true, false) => ra2h,
            (false, true) => slb,
            (false, false) => front,
        };

        foreach (var sketch in CobLikeFeatureCatalog.AllFrontSketches(shank))
        {
            if (string.Equals(sketch, active, StringComparison.OrdinalIgnoreCase))
                plan.Activate(sketch);
            else
                plan.Deactivate(sketch);
        }
    }

    /// <summary>
    /// Returns the correct left overlay sketch name based on VW/VR state.
    /// </summary>
    private static string ResolveLeftSketch(CobLikeFacts facts, bool pgb)
    {
        if (!facts.HasVw)
            return pgb ? CobLikeFeatureCatalog.LeftSketchPgb : CobLikeFeatureCatalog.LeftSketchFg;

        if (facts.HasLargeOverlayVrCase)
            return facts.AreEqual("VW", "W")
                ? CobLikeFeatureCatalog.VwLeftCase4
                : CobLikeFeatureCatalog.VwLeftCase3;

        return facts.AreEqual("VW", "W")
            ? CobLikeFeatureCatalog.VwLeftCase2
            : CobLikeFeatureCatalog.VwLeftCase1;
    }

    /// <summary>
    /// Activates the ERW overlay sketch matching the shank type; deactivates the other.
    /// </summary>
    private static void ActivateErwOverlaySketch(FeaturePlanBuilder plan, CobLikeShankType shank)
    {
        foreach (var sketch in CobLikeFeatureCatalog.AllErwOverlaySketches())
        {
            if (string.Equals(sketch, CobLikeFeatureCatalog.ErwOverlaySketch(shank), StringComparison.OrdinalIgnoreCase))
                plan.Activate(sketch);
            else
                plan.Deactivate(sketch);
        }
    }

    /// <summary>
    /// Activates exactly one of the nine foot-width overlay sketches
    /// ({C|VG|G}_FOOT_{W|VW|W2}_overlay_sketch) based on the foot option and the
    /// smallest positive value among W, VW, and W2. Deactivates the other eight.
    /// </summary>
    private static void ActivateFootWidthSketch(FeaturePlanBuilder plan, CobLikeFacts facts, CobLikeFootOption foot)
    {
        var w = facts.Facts.NominalOrZero("W");
        var vw = facts.Facts.NominalOrZero("VW");
        var w2 = facts.Facts.NominalOrZero("W2");

        var active = CobLikeFeatureCatalog.FootWidthSketch(foot, w, vw, w2);

        foreach (var sketch in CobLikeFeatureCatalog.AllFootWidthSketches())
        {
            if (string.Equals(sketch, active, StringComparison.OrdinalIgnoreCase))
                plan.Activate(sketch);
            else
                plan.Deactivate(sketch);
        }
    }

    // =========================================================================
    // Overlay cut profile override
    // =========================================================================

    /// <summary>
    /// Allows the overlay cut profile (std / non-std) to be overridden via the
    /// job context, without changing the wedge data itself.
    /// </summary>
    private void ApplyOverlayCutProfileOverride(FeatureRuleContext context, FeaturePlanBuilder plan)
    {
        if (context.DrawingType != DrawingType.Overlay) return;

        var profile = ResolveOverlayCutProfile(context);
        if (profile is null) return;

        if (profile is "default_config" or "std_cut")
        {
            plan.Activate(CobLikeFeatureCatalog.OverlayCutStandard());
            plan.ForceSuppress(CobLikeFeatureCatalog.OverlayCutNonStandard());
            Logger.Info($"[{LogPrefix}] Overlay cut profile '{profile}' → standard cut.");
        }
        else if (profile == "non_std_cut")
        {
            plan.Activate(CobLikeFeatureCatalog.OverlayCutNonStandard());
            plan.ForceSuppress(CobLikeFeatureCatalog.OverlayCutStandard());
            Logger.Info($"[{LogPrefix}] Overlay cut profile 'non_std_cut' → non-standard cut.");
        }
    }

    private static string? ResolveOverlayCutProfile(FeatureRuleContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.FeatureRuleProfile))
            return context.FeatureRuleProfile.Trim();

        return context.TargetConfigurationName?.Trim().ToLowerInvariant() switch
        {
            "non_std_cut" => "non_std_cut",
            "std_cut" => "std_cut",
            "default" => "default_config",
            _ => null
        };
    }

    // =========================================================================
    // Protected helpers for derived classes
    // =========================================================================

    protected static void ActivateFeature(FeaturePlanBuilder plan, string baseName, CobLikeShankType shank)
        => plan.Activate(CobLikeFeatureCatalog.FeatureNames(baseName, shank));

    protected static void SuppressFeature(FeaturePlanBuilder plan, string baseName, CobLikeShankType shank)
        => plan.ForceSuppress(CobLikeFeatureCatalog.FeatureNames(baseName, shank));
}
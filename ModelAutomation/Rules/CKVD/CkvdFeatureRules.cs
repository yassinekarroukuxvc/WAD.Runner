using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

using System;

using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

public sealed class CkvdFeatureRules : IFeatureRuleSet
{
    private const string AnnotationTopPlan =
        "annotation_top_plan";

    private const string AnnotationRightPlan =
        "annotation_right_plan";

    private const string PartAxis =
        "part_axis";

    private const string TdFeature =
        "td_feature";

    private const string IsaFeature =
        "isa_feature";

    private const string VrFeature =
        "vr_feature";

    private const string StyleAFeature =
        "style_a_feature";

    private const string StyleBFeature =
        "style_b_feature";

    private const string StyleAFrBrFeature =
        "style_a_fr_br_feature";

    private const string StyleAFrBrCutFeature =
        "style_a_fr_br_cut_feature";

    private const string StyleBFrBrFeature =
        "style_b_fr_br_feature";

    private const string StyleBFrBrCutFeature =
        "style_b_fr_br_cut_feature";

    private const string RefPoint =
        "ref_point";

    private const string RefPointNonStdCut =
        "ref_point_non_std_cut";

    private const string RefPointA =
        "ref_point_a";

    private const string RefPointB =
        "ref_point_b";

    private const string CutPlanFeature =
        "cut_plan_feature";

    private const string CutFeature =
        "cut_feature";

    private const string NonStdCutPlanFeature =
        "non_std_cut_plan_feature";

    private const string NonStdCutFeature =
        "non_std_cut_feature";

    private const string WPgbOverlaySketch =
        "w_pgb_overlay_sketch";

    private const string StyleAFlPgbOverlaySketch =
        "style_a_fl_pgb_overlay_sketch";

    private const string StyleBFlPgbOverlaySketch =
        "style_b_fl_pgb_overlay_sketch";

    private const string VwCase1PgbOverlaySketch =
        "vw_case1_pgb_overlay_sketch";

    private const string VwCase2PgbOverlaySketch =
        "vw_case2_pgb_overlay_sketch";

    private const string StyleAFrBrOverlaySketch =
        "style_a_fr_br_overlay_sketch";

    private const string StyleBFrBrOverlaySketch =
        "style_b_fr_br_overlay_sketch";

    private const string VgFgOverlaySketch =
        "vg_fg_overlay_sketch";

    private const string WFgOverlaySketch =
        "w_fg_overlay_sketch";

    private const string StyleAFlFgOverlaySketch =
        "style_a_fl_fg_overlay_sketch";

    private const string StyleBFlFgOverlaySketch =
        "style_b_fl_fg_overlay_sketch";

    private const string VwCase1FgOverlaySketch =
        "vw_case1_fg_overlay_sketch";

    private const string VwCase2FgOverlaySketch =
        "vw_case2_fg_overlay_sketch";

    /*
     * These features are always active regardless of CKVD style.
     *
     * The style-specific front annotation planes are intentionally
     * not managed by these rules. Their suppression state is left
     * unchanged in the SolidWorks model.
     */
    private static readonly string[] AlwaysOn =
    {
        AnnotationTopPlan,
        AnnotationRightPlan,
        PartAxis,
        TdFeature,
        IsaFeature
    };

    private static readonly string[] StyleFeatures =
    {
        StyleAFeature,
        StyleBFeature
    };

    private static readonly string[] OverlayReferenceNames =
    {
        RefPoint,
        RefPointNonStdCut,
        RefPointA,
        RefPointB
    };

    private static readonly string[] StandardCutNames =
    {
        CutPlanFeature,
        CutFeature
    };

    private static readonly string[] NonStandardCutNames =
    {
        NonStdCutPlanFeature,
        NonStdCutFeature
    };

    private static readonly string[] PgbStyleFlOverlaySketches =
    {
        StyleAFlPgbOverlaySketch,
        StyleBFlPgbOverlaySketch
    };

    private static readonly string[] PgbVwCaseOverlaySketches =
    {
        VwCase1PgbOverlaySketch,
        VwCase2PgbOverlaySketch
    };

    private static readonly string[] FgStyleFrBrOverlaySketches =
    {
        StyleAFrBrOverlaySketch,
        StyleBFrBrOverlaySketch
    };

    private static readonly string[] FgStyleFlOverlaySketches =
    {
        StyleAFlFgOverlaySketch,
        StyleBFlFgOverlaySketch
    };

    private static readonly string[] FgVwCaseOverlaySketches =
    {
        VwCase1FgOverlaySketch,
        VwCase2FgOverlaySketch
    };

    /*
     * These names are managed for Production, Customer and Overlay.
     * Features that are known but not activated by the plan are
     * suppressed by FeaturePlanBuilder.
     */
    private static readonly string[] ManagedNames =
    {
        AnnotationTopPlan,
        AnnotationRightPlan,
        PartAxis,
        TdFeature,
        IsaFeature,
        VrFeature,
        StyleAFeature,
        StyleBFeature,
        StyleAFrBrFeature,
        StyleAFrBrCutFeature,
        StyleBFrBrFeature,
        StyleBFrBrCutFeature
    };

    private static readonly string[] OverlayManagedNames =
    {
        RefPoint,
        RefPointNonStdCut,
        RefPointA,
        RefPointB,
        CutPlanFeature,
        CutFeature,
        NonStdCutPlanFeature,
        NonStdCutFeature,
        WPgbOverlaySketch,
        StyleAFlPgbOverlaySketch,
        StyleBFlPgbOverlaySketch,
        VwCase1PgbOverlaySketch,
        VwCase2PgbOverlaySketch,
        StyleAFrBrOverlaySketch,
        StyleBFrBrOverlaySketch,
        VgFgOverlaySketch,
        WFgOverlaySketch,
        StyleAFlFgOverlaySketch,
        StyleBFlFgOverlaySketch,
        VwCase1FgOverlaySketch,
        VwCase2FgOverlaySketch
    };

    public ModelRuleRunner.FeaturePlan Build(
        WedgeData wedge,
        FeatureRuleContext context)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var facts = new WedgeFacts(wedge);

        Logger.Info(
            "[CkvdFeatureRules] Build -> " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, " +
            $"ruleProfile={context.FeatureRuleProfile ?? "(none)"}.");

        return BuildDrawingPlan(
            facts,
            context);
    }

    private static ModelRuleRunner.FeaturePlan BuildDrawingPlan(
        WedgeFacts facts,
        FeatureRuleContext context)
    {
        var style = ResolveStyle(facts);

        var isOverlay =
            context.DrawingType == DrawingType.Overlay;

        /*
         * Preserve the existing VR feature rule, including VRA.
         */
        var hasVrFeatureFamily = HasAnyPositiveNominal(
            facts,
            "VR",
            "VW",
            "VRR",
            "VRA");

        /*
         * The new CKVD overlay case selection is driven specifically
         * by VR, VRR and VW, as defined by the overlay specification.
         */
        var hasOverlayVrFamily = HasAnyPositiveNominal(
            facts,
            "VR",
            "VRR",
            "VW");

        var overlayVwCase = ResolveOverlayVwCase(
            facts,
            hasOverlayVrFamily);

        var plan = new FeaturePlanBuilder()
            .Know(ManagedNames)
            .Activate(AlwaysOn)
            .ForceSuppress(
                SwNames.EngravingFeature,
                SwNames.EngravingSketch);

        if (isOverlay)
        {
            plan.Know(OverlayManagedNames);
            plan.Activate(OverlayReferenceNames);

            ApplyOverlayCutRules(
                plan,
                context);

            ApplyOverlaySketchRules(
                plan,
                context.Subclass,
                style,
                overlayVwCase);
        }

        /*
         * vr_feature is enabled when at least one member of the
         * existing VR dimension family has a positive nominal value.
         */
        if (hasVrFeatureFamily)
            plan.Activate(VrFeature);

        /*
         * Only the feature belonging to the resolved CKVD style
         * may remain active.
         */
        plan.ActivateOnly(
            style == CkvdStyle.StyleA
                ? StyleAFeature
                : StyleBFeature,
            StyleFeatures);

        /*
         * PGB:
         * All four FR/BR features stay suppressed because they are
         * known by the plan but none of them is activated.
         *
         * FG:
         * Activate the FR/BR feature and cut belonging to the
         * selected CKVD style.
         */
        if (context.Subclass == WedgeSubclass.FG)
        {
            if (style == CkvdStyle.StyleA)
            {
                plan.Activate(
                    StyleAFrBrFeature,
                    StyleAFrBrCutFeature);
            }
            else
            {
                plan.Activate(
                    StyleBFrBrFeature,
                    StyleBFrBrCutFeature);
            }

            /*
             * FG Overlay follows the same TIP rule as FG
             * Production and Customer.
             */
            AddTipGuardPlan(
                facts,
                plan);
        }

        Logger.Info(
            "[CkvdFeatureRules] Drawing plan -> " +
            $"drawingType={context.DrawingType}, " +
            $"subclass={context.Subclass}, " +
            $"style={style}, " +
            $"VR feature family present={hasVrFeatureFamily}, " +
            $"overlay VR/VRR/VW present={hasOverlayVrFamily}, " +
            $"overlay VW case={overlayVwCase}, " +
            $"FR/BR features active={context.Subclass == WedgeSubclass.FG}.");

        return plan.Build();
    }

    private static void ApplyOverlayCutRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context)
    {
        var mode = ResolveOverlayCutMode(context);

        plan.Deactivate(StandardCutNames);
        plan.Deactivate(NonStandardCutNames);

        if (mode == OverlayCutMode.NonStandard)
        {
            plan.Activate(NonStandardCutNames);
        }
        else
        {
            plan.Activate(StandardCutNames);
        }

        Logger.Info(
            "[CkvdFeatureRules] Overlay cut selection -> " +
            $"mode={mode}, config={context.TargetConfigurationName}, " +
            $"profile={context.FeatureRuleProfile ?? "(none)"}.");
    }

    private static OverlayCutMode ResolveOverlayCutMode(
        FeatureRuleContext context)
    {
        var token =
            string.IsNullOrWhiteSpace(context.FeatureRuleProfile)
                ? context.TargetConfigurationName
                : context.FeatureRuleProfile;

        var normalized =
            (token ?? string.Empty)
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToLowerInvariant();

        return normalized switch
        {
            "overlay_non_std_cut" =>
                OverlayCutMode.NonStandard,

            OverlayCutProfiles.NonStandardCut =>
                OverlayCutMode.NonStandard,

            "default" =>
                OverlayCutMode.Standard,

            "overlay" =>
                OverlayCutMode.Standard,

            OverlayCutProfiles.DefaultConfiguration =>
                OverlayCutMode.Standard,

            OverlayCutProfiles.StandardCut =>
                OverlayCutMode.Standard,

            _ => ResolveUnknownOverlayCutMode(
                normalized)
        };
    }

    private static OverlayCutMode ResolveUnknownOverlayCutMode(
        string normalizedProfile)
    {
        Logger.Warn(
            "[CkvdFeatureRules] Unknown overlay configuration/profile " +
            $"'{normalizedProfile}'. Preserving the previous CKVD " +
            "behavior by using the standard cut features.");

        return OverlayCutMode.Standard;
    }

    private static void ApplyOverlaySketchRules(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        CkvdStyle style,
        OverlayVwCase overlayVwCase)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            plan.Activate(WPgbOverlaySketch);

            plan.ActivateOnly(
                style == CkvdStyle.StyleA
                    ? StyleAFlPgbOverlaySketch
                    : StyleBFlPgbOverlaySketch,
                PgbStyleFlOverlaySketches);

            ActivatePgbVwCaseSketch(
                plan,
                overlayVwCase);

            return;
        }

        plan.Activate(
            VgFgOverlaySketch,
            WFgOverlaySketch);

        plan.ActivateOnly(
            style == CkvdStyle.StyleA
                ? StyleAFrBrOverlaySketch
                : StyleBFrBrOverlaySketch,
            FgStyleFrBrOverlaySketches);

        plan.ActivateOnly(
            style == CkvdStyle.StyleA
                ? StyleAFlFgOverlaySketch
                : StyleBFlFgOverlaySketch,
            FgStyleFlOverlaySketches);

        ActivateFgVwCaseSketch(
            plan,
            overlayVwCase);
    }

    private static void ActivatePgbVwCaseSketch(
        FeaturePlanBuilder plan,
        OverlayVwCase overlayVwCase)
    {
        if (overlayVwCase == OverlayVwCase.None)
            return;

        plan.ActivateOnly(
            overlayVwCase == OverlayVwCase.Case1
                ? VwCase1PgbOverlaySketch
                : VwCase2PgbOverlaySketch,
            PgbVwCaseOverlaySketches);
    }

    private static void ActivateFgVwCaseSketch(
        FeaturePlanBuilder plan,
        OverlayVwCase overlayVwCase)
    {
        if (overlayVwCase == OverlayVwCase.None)
            return;

        plan.ActivateOnly(
            overlayVwCase == OverlayVwCase.Case1
                ? VwCase1FgOverlaySketch
                : VwCase2FgOverlaySketch,
            FgVwCaseOverlaySketches);
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasOverlayVrFamily)
    {
        if (!hasOverlayVrFamily ||
            !facts.TryGetLengthMm(
                "VW",
                out var vwMillimeters) ||
            vwMillimeters <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[CkvdFeatureRules] VW is present but W is missing or " +
                "not a length. No CKVD VW overlay case sketch was selected.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        if (vwMillimeters >
            wMillimeters + WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case2;
        }

        Logger.Warn(
            "[CkvdFeatureRules] CKVD overlay received VW < W " +
            $"(VW={vwMillimeters} mm, W={wMillimeters} mm). " +
            "Only VW = W (Case 1) and VW > W (Case 2) are defined; " +
            "no VW case sketch was selected.");

        return OverlayVwCase.None;
    }

    private static CkvdStyle ResolveStyle(
        WedgeFacts facts)
    {
        var raw = facts.NormalizedPropertyToken(
            "Wed-Type",
            "Wed_Type",
            "Wed Type",
            "Shank_Type",
            "shank_type");

        if (string.Equals(
                raw,
                "LW_STYLE_A_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdStyle.StyleA;
        }

        if (string.Equals(
                raw,
                "LW_STYLE_B_CKVD",
                StringComparison.OrdinalIgnoreCase))
        {
            return CkvdStyle.StyleB;
        }

        throw new InvalidOperationException(
            "Unable to resolve the CKVD shank style from Wed-Type. " +
            "Expected 'LW_STYLE_A_CKVD' or " +
            $"'LW_STYLE_B_CKVD', but received '{raw}'.");
    }

    private static bool HasAnyPositiveNominal(
        WedgeFacts facts,
        params string[] dimensionKeys)
    {
        foreach (var key in dimensionKeys)
        {
            if (facts.HasPositive(key))
                return true;
        }

        return false;
    }

    private static void AddTipGuardPlan(
        WedgeFacts facts,
        FeaturePlanBuilder plan)
    {
        if (!facts.TryGetLengthMm(
                "TIP",
                out var tipMm))
        {
            Logger.Info(
                "[CkvdFeatureRules] TIP not present/mm -> " +
                "TIP guard skipped.");

            return;
        }

        plan.Know(SwNames.SketchCrmet);

        if (tipMm <= WedgeFacts.DefaultPositiveEpsilon)
        {
            plan.ForceSuppress(
                SwNames.SketchCrmet);

            Logger.Info(
                $"[CkvdFeatureRules] TIP={tipMm} mm -> " +
                $"suppress '{SwNames.SketchCrmet}'.");
        }
        else
        {
            plan.Activate(
                SwNames.SketchCrmet);

            Logger.Info(
                $"[CkvdFeatureRules] TIP={tipMm} mm -> " +
                $"unsuppress '{SwNames.SketchCrmet}'.");
        }
    }

    private enum CkvdStyle
    {
        StyleA,
        StyleB
    }

    private enum OverlayCutMode
    {
        Standard,
        NonStandard
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
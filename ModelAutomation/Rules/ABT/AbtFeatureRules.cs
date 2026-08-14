using System;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.ABT;

/// <summary>
/// ABT feature rules.
///
/// Shank selection:
///     SW_STD    -> std_* family
///     SW_180REV -> rev_* family
///
/// PGB:
///     No feed-hole or foot-option features.
///
/// VR:
///     Active when VR, VW, VRR and VRA are all > 0.
///
/// SLB:
///     Active when VBL, VBLR and T are all > 0.
///
/// W2:
///     Active when W2 > 0.
///
/// RA2:
///     Active when RA2 and RA2H are both > 0.
///
/// FG feed-hole:
///     STD  -> STD hole family
///     Oval -> Oval family
///     Slot -> Slot family
///
/// FG foot:
///     C / C+CBR -> C family
///     VG        -> VG family
///     G         -> G family
///     CC        -> no ABT foot features were specified.
///
/// Overlay:
///     Activates the selected shank cut/reference family.
///     PGB and FG use different W overlay sketches.
///     VW Case 1 -> VW = W
///     VW Case 2 -> VW != W
///     T Case 1  -> no VBL and no RA2
///     T Case 2  -> VBL only
///     T Case 3  -> RA2 only
///     T Case 4  -> VBL and RA2
/// </summary>
public sealed class AbtFeatureRules : IFeatureRuleSet
{
    // ================================================================
    // STD SHANK - ALWAYS ON
    // ================================================================

    private static readonly string[] StdAlwaysOnNames =
    {
        "td_std_feature", "td_std_sketch",
        "isa_std_feature", "isa_std_sketch",
        "ba_std_feature", "ba_std_sketch",
        "fro_std_feature", "fro_std_sketch",
        "erw_std_feature", "erw_std_sketch",
        "core_fillet_std_feature", "std_round_br"
    };

    private static readonly string[] StdVrNames =
    {
        "vr_std_feature", "vr_std_sketch"
    };

    private static readonly string[] StdSlbNames =
    {
        "slb_std_feature", "slb_std_sketch"
    };

    private static readonly string[] StdW2Names =
    {
        "w2_std_feature", "w2_std_sketch"
    };

    private static readonly string[] StdRa2Names =
    {
        "ra2_std_feature", "ra2_std_sketch"
    };

    // ================================================================
    // STD SHANK - FEED HOLE
    // ================================================================

    private static readonly string[] StdHoleNames =
    {
        "std_hole_feature", "std_hole_sketch",
        "std_hole_cut_feature", "std_hole_cut_sketch",
        "std_hole_combine"
    };

    private static readonly string[] StdOvalNames =
    {
        "std_oval_plan",
        "std_oval_feature", "std_oval_sketch",
        "std_oval_cut_feature", "std_oval_cut_sketch",
        "std_oval_combine"
    };

    private static readonly string[] StdSlotNames =
    {
        "std_slot_plan",
        "std_slot_feature", "std_slot_sketch",
        "std_slot_cut_feature", "std_slot_cut_sketch",
        "std_slot_combine"
    };

    private static readonly string[] StdFeedHoleManagedNames =
    {
        "std_hole_feature", "std_hole_sketch", "std_hole_cut_feature", "std_hole_cut_sketch", "std_hole_combine",
        "std_oval_plan", "std_oval_feature", "std_oval_sketch", "std_oval_cut_feature", "std_oval_cut_sketch", "std_oval_combine",
        "std_slot_plan", "std_slot_feature", "std_slot_sketch", "std_slot_cut_feature", "std_slot_cut_sketch", "std_slot_combine"
    };

    // ================================================================
    // STD SHANK - FOOT
    // ================================================================

    private static readonly string[] StdCFootBaseNames =
    {
        "std_c_feature", "std_c_sketch"
    };

    private const string StdCFrFeature =
        "std_fr_c_feature";

    private const string StdCBrFeature =
        "std_br_c_feature";

    private const string StdCCbrFeature =
        "std_cbr_c_feature";

    private const string StdCCbrCoreFeature =
        "std_cbr_c_core_feature";

    private const string StdCRoundBrFeature =
        "std_c_round_br_feature";

    private static readonly string[] StdVgFootNames =
    {
        "std_vg_feature", "std_vg_sketch",
        "std_fr_vg_feature", "std_br_vg_feature"
    };

    private static readonly string[] StdGFootNames =
    {
        "std_g_feature", "std_g_sketch",
        "std_g_fr_feature", "std_g_br_feature"
    };

    private static readonly string[] StdFootManagedNames =
    {
        "std_c_feature", "std_c_sketch", "std_fr_c_feature", "std_cbr_c_feature",
        "std_cbr_c_core_feature", "std_br_c_feature", "std_c_round_br_feature",
        "std_vg_feature", "std_vg_sketch", "std_fr_vg_feature", "std_br_vg_feature",
        "std_g_feature", "std_g_sketch", "std_g_fr_feature", "std_g_br_feature"
    };

    // ================================================================
    // STD SHANK - OVERLAY
    // ================================================================

    private static readonly string[] StdRightOverlayCutNames =
    {
        "std_ref_point_right", "std_right_cut_plan", "std_right_cut"
    };

    private static readonly string[] StdLeftOverlayCutNames =
    {
        "std_ref_point_left", "std_left_cut_plan", "std_left_cut"
    };

    private const string StdWPgbOverlaySketch =
        "std_w_pgb_overlay_sketch";

    private const string StdWFgOverlaySketch =
        "std_w_fg_overlay_sketch";

    private static readonly string[] StdVwCaseOverlaySketches =
    {
        "std_vw_case1_overlay_sketch", "std_vw_case2_overlay_sketch"
    };

    private static readonly string[] StdTCaseOverlaySketches =
    {
        "std_t_case1_overlay_sketch", "std_t_case2_overlay_sketch",
        "std_t_case3_overlay_sketch", "std_t_case4_overlay_sketch"
    };

    private static readonly string[] StdFootOverlaySketches =
    {
        "std_c_overlay_sketch", "std_vg_overlay_sketch", "std_g_overlay_sketch"
    };

    private static readonly string[] StdOverlayManagedNames =
    {
        "std_ref_point_right", "std_right_cut_plan", "std_right_cut",
        "std_ref_point_left", "std_left_cut_plan", "std_left_cut",
        "std_w_pgb_overlay_sketch", "std_w_fg_overlay_sketch",
        "std_vw_case1_overlay_sketch", "std_vw_case2_overlay_sketch",
        "std_t_case1_overlay_sketch", "std_t_case2_overlay_sketch",
        "std_t_case3_overlay_sketch", "std_t_case4_overlay_sketch",
        "std_c_overlay_sketch", "std_vg_overlay_sketch", "std_g_overlay_sketch"
    };

    // ================================================================
    // REV SHANK - ALWAYS ON
    // ================================================================

    private static readonly string[] RevAlwaysOnNames =
    {
        "td_rev_feature", "td_rev_sketch",
        "isa_rev_feature", "isa_rev_sketch",
        "ba_rev_feature", "ba_rev_sketch",
        "fro_rev_feature",
        "erw_rev_feature", "erw_rev_sketch",
        "core_fillet_rev_feature", "rev_round_br"
    };

    private static readonly string[] RevVrNames =
    {
        "vr_rev_feature", "vr_rev_sketch"
    };

    private static readonly string[] RevSlbNames =
    {
        "slb_rev_feature", "slb_rev_sketch"
    };

    private static readonly string[] RevW2Names =
    {
        "w2_rev_feature", "w2_rev_sketch"
    };

    private static readonly string[] RevRa2Names =
    {
        "ra2_rev_feature", "ra2_rev_sketch"
    };

    // ================================================================
    // REV SHANK - FEED HOLE
    // ================================================================

    private static readonly string[] RevHoleNames =
    {
        "rev_hole_feature", "rev_hole_sketch",
        "rev_hole_cut_feature", "rev_hole_cut_sketch",
        "rev_hole_combine"
    };

    private static readonly string[] RevOvalNames =
    {
        "rev_oval_plan",
        "rev_oval_feature", "rev_oval_sketch",
        "rev_oval_cut_feature", "rev_oval_cut_sketch",
        "rev_oval_combine_feature"
    };

    private static readonly string[] RevSlotNames =
    {
        "rev_slot_plan",
        "rev_slot_feature", "rev_slot_sketch",
        "rev_slot_cut_feature", "rev_slot_cut_sketch",
        "rev_slot_combine_feature"
    };

    private static readonly string[] RevFeedHoleManagedNames =
    {
        "rev_hole_feature", "rev_hole_sketch", "rev_hole_cut_feature", "rev_hole_cut_sketch", "rev_hole_combine",
        "rev_oval_plan", "rev_oval_feature", "rev_oval_sketch", "rev_oval_cut_feature", "rev_oval_cut_sketch", "rev_oval_combine_feature",
        "rev_slot_plan", "rev_slot_feature", "rev_slot_sketch", "rev_slot_cut_feature", "rev_slot_cut_sketch", "rev_slot_combine_feature"
    };

    // ================================================================
    // REV SHANK - FOOT
    // ================================================================

    private static readonly string[] RevCFootBaseNames =
    {
        "rev_c_feature", "rev_c_sketch"
    };

    private const string RevCFrFeature =
        "rev_c_fr_feature";

    private const string RevCBrFeature =
        "rev_c_br_feature";

    private const string RevCCbrFeature =
        "rev_c_cbr_feature";

    private const string RevCCbrCoreFeature =
        "rev_cbr_c_core_feature";

    private const string RevCRoundBrFeature =
        "rev_c_round_br_feature";

    private static readonly string[] RevVgFootNames =
    {
        "rev_vg_feature", "rev_vg_sketch",
        "rev_vg_fr_feature", "rev_vg_br_feature"
    };

    private static readonly string[] RevGFootNames =
    {
        "rev_g_feature", "rev_g_sketch",
        "rev_g_fr_feature", "rev_g_br_feature"
    };

    private static readonly string[] RevFootManagedNames =
    {
        "rev_c_feature", "rev_c_sketch", "rev_c_fr_feature", "rev_c_br_feature",
        "rev_c_cbr_feature", "rev_cbr_c_core_feature", "rev_c_round_br_feature",
        "rev_vg_feature", "rev_vg_sketch", "rev_vg_fr_feature", "rev_vg_br_feature",
        "rev_g_feature", "rev_g_sketch", "rev_g_fr_feature", "rev_g_br_feature"
    };

    // ================================================================
    // REV SHANK - OVERLAY
    // ================================================================

    private static readonly string[] RevRightOverlayCutNames =
    {
        "rev_ref_point_right", "rev_right_cut_plan", "rev_right_cut"
    };

    private static readonly string[] RevLeftOverlayCutNames =
    {
        "rev_ref_point_left", "rev_left_cut_plan", "rev_left_cut"
    };

    private const string RevWPgbOverlaySketch =
        "rev_w_pgb_overlay_sketch";

    private const string RevWFgOverlaySketch =
        "rev_w_fg_overlay_sketch";

    private static readonly string[] RevVwCaseOverlaySketches =
    {
        "rev_vw_case1_overlay_sketch", "rev_vw_case2_overlay_sketch"
    };

    private static readonly string[] RevTCaseOverlaySketches =
    {
        "rev_t_case1_overlay_sketch", "rev_t_case2_overlay_sketch",
        "rev_t_case3_overlay_sketch", "rev_t_case4_overlay_sketch"
    };

    private static readonly string[] RevFootOverlaySketches =
    {
        "rev_c_overlay_sketch", "rev_vg_overlay_sketch", "rev_g_overlay_sketch"
    };

    private static readonly string[] RevOverlayManagedNames =
    {
        "rev_ref_point_right", "rev_right_cut_plan", "rev_right_cut",
        "rev_ref_point_left", "rev_left_cut_plan", "rev_left_cut",
        "rev_w_pgb_overlay_sketch", "rev_w_fg_overlay_sketch",
        "rev_vw_case1_overlay_sketch", "rev_vw_case2_overlay_sketch",
        "rev_t_case1_overlay_sketch", "rev_t_case2_overlay_sketch",
        "rev_t_case3_overlay_sketch", "rev_t_case4_overlay_sketch",
        "rev_c_overlay_sketch", "rev_vg_overlay_sketch", "rev_g_overlay_sketch"
    };

    // ================================================================
    // ENTRY POINT
    // ================================================================

    public ModelRuleRunner.FeaturePlan Build(
        WedgeData wedge,
        FeatureRuleContext context)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var facts =
            new WedgeFacts(wedge);

        var shank =
            ResolveShankType(
                facts);

        Logger.Info(
            "[AbtFeatureRules] Build -> " +
            $"shank={shank}, " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, " +
            $"ruleProfile={context.FeatureRuleProfile ?? "(none)"}.");

        return BuildDrawingPlan(
            facts,
            context,
            shank);
    }

    // ================================================================
    // DRAWING PLAN
    // ================================================================

    private static ModelRuleRunner.FeaturePlan BuildDrawingPlan(
        WedgeFacts facts,
        FeatureRuleContext context,
        AbtShankType shank)
    {
        var isOverlay =
            context.DrawingType == DrawingType.Overlay;

        var hasCompleteVrFamily =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW",
                "VRR",
                "VRA");

        var hasSlb =
            HasAllPositiveNominal(
                facts,
                "VBL",
                "VBLR",
                "T");

        var hasW2 =
            facts.HasPositive(
                "W2");

        var hasRa2 =
            HasAllPositiveNominal(
                facts,
                "RA2",
                "RA2H");

        var hasOverlayVbl =
            facts.HasPositive(
                "VBL");

        var hasOverlayRa2 =
            facts.HasPositive(
                "RA2");

        var overlayVwCase =
            ResolveOverlayVwCase(
                facts);

        var feedHoleType =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFeedHoleType(facts)
                : FeedHoleType.NotApplicable;

        var footOption =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFootOption(facts)
                : FootOptionType.NotApplicable;

        var plan =
            new FeaturePlanBuilder()
                .Know(StdAlwaysOnNames)
                .Know(StdVrNames)
                .Know(StdSlbNames)
                .Know(StdW2Names)
                .Know(StdRa2Names)
                .Know(StdFeedHoleManagedNames)
                .Know(StdFootManagedNames)
                .Know(StdOverlayManagedNames)
                .Know(RevAlwaysOnNames)
                .Know(RevVrNames)
                .Know(RevSlbNames)
                .Know(RevW2Names)
                .Know(RevRa2Names)
                .Know(RevFeedHoleManagedNames)
                .Know(RevFootManagedNames)
                .Know(RevOverlayManagedNames)
                .ForceSuppress(
                    SwNames.EngravingFeature,
                    SwNames.EngravingSketch);

        if (shank == AbtShankType.Std)
        {
            ForceSuppressRevShank(
                plan);

            ApplyStdBaseRules(
                plan,
                hasCompleteVrFamily,
                hasSlb,
                hasW2,
                hasRa2);

            ApplyStdSubclassRules(
                plan,
                facts,
                context.Subclass,
                feedHoleType,
                footOption);

            if (isOverlay)
            {
                ApplyStdOverlayRules(
                    plan,
                    context,
                    footOption,
                    hasOverlayVbl,
                    hasOverlayRa2,
                    overlayVwCase);
            }
            else
            {
                plan.ForceSuppress(
                    StdOverlayManagedNames);
            }
        }
        else
        {
            ForceSuppressStdShank(
                plan);

            ApplyRevBaseRules(
                plan,
                hasCompleteVrFamily,
                hasSlb,
                hasW2,
                hasRa2);

            ApplyRevSubclassRules(
                plan,
                facts,
                context.Subclass,
                feedHoleType,
                footOption);

            if (isOverlay)
            {
                ApplyRevOverlayRules(
                    plan,
                    context,
                    footOption,
                    hasOverlayVbl,
                    hasOverlayRa2,
                    overlayVwCase);
            }
            else
            {
                plan.ForceSuppress(
                    RevOverlayManagedNames);
            }
        }

        Logger.Info(
            "[AbtFeatureRules] Drawing plan -> " +
            $"shank={shank}, " +
            $"drawingType={context.DrawingType}, " +
            $"subclass={context.Subclass}, " +
            $"feedHole={feedHoleType}, " +
            $"footOption={footOption}, " +
            $"VR family={hasCompleteVrFamily}, " +
            $"SLB={hasSlb}, " +
            $"W2={hasW2}, " +
            $"RA2 family={hasRa2}, " +
            $"overlay VBL={hasOverlayVbl}, " +
            $"overlay RA2={hasOverlayRa2}, " +
            $"overlay VW case={overlayVwCase}.");

        return plan.Build();
    }

    // ================================================================
    // BASE FEATURE RULES
    // ================================================================

    private static void ApplyStdBaseRules(
        FeaturePlanBuilder plan,
        bool hasCompleteVrFamily,
        bool hasSlb,
        bool hasW2,
        bool hasRa2)
    {
        plan.Activate(
            StdAlwaysOnNames);

        plan.Deactivate(
            StdVrNames);

        plan.Deactivate(
            StdSlbNames);

        plan.Deactivate(
            StdW2Names);

        plan.Deactivate(
            StdRa2Names);

        if (hasCompleteVrFamily)
            plan.Activate(StdVrNames);

        if (hasSlb)
            plan.Activate(StdSlbNames);

        if (hasW2)
            plan.Activate(StdW2Names);

        if (hasRa2)
            plan.Activate(StdRa2Names);
    }

    private static void ApplyRevBaseRules(
        FeaturePlanBuilder plan,
        bool hasCompleteVrFamily,
        bool hasSlb,
        bool hasW2,
        bool hasRa2)
    {
        plan.Activate(
            RevAlwaysOnNames);

        plan.Deactivate(
            RevVrNames);

        plan.Deactivate(
            RevSlbNames);

        plan.Deactivate(
            RevW2Names);

        plan.Deactivate(
            RevRa2Names);

        if (hasCompleteVrFamily)
            plan.Activate(RevVrNames);

        if (hasSlb)
            plan.Activate(RevSlbNames);

        if (hasW2)
            plan.Activate(RevW2Names);

        if (hasRa2)
            plan.Activate(RevRa2Names);
    }

    // ================================================================
    // SUBCLASS RULES
    // ================================================================

    private static void ApplyStdSubclassRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        WedgeSubclass subclass,
        FeedHoleType feedHoleType,
        FootOptionType footOption)
    {
        plan.Deactivate(
            StdFeedHoleManagedNames);

        plan.Deactivate(
            StdFootManagedNames);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                StdFeedHoleManagedNames);

            plan.ForceSuppress(
                StdFootManagedNames);

            Logger.Info(
                "[AbtFeatureRules] STD PGB -> all feed-hole and " +
                "foot-option features suppressed.");

            return;
        }

        ApplyStdFeedHoleRules(
            plan,
            feedHoleType);

        ApplyStdFootRules(
            plan,
            facts,
            footOption);
    }

    private static void ApplyRevSubclassRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        WedgeSubclass subclass,
        FeedHoleType feedHoleType,
        FootOptionType footOption)
    {
        plan.Deactivate(
            RevFeedHoleManagedNames);

        plan.Deactivate(
            RevFootManagedNames);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                RevFeedHoleManagedNames);

            plan.ForceSuppress(
                RevFootManagedNames);

            Logger.Info(
                "[AbtFeatureRules] REV PGB -> all feed-hole and " +
                "foot-option features suppressed.");

            return;
        }

        ApplyRevFeedHoleRules(
            plan,
            feedHoleType);

        ApplyRevFootRules(
            plan,
            facts,
            footOption);
    }

    // ================================================================
    // FEED-HOLE RULES
    // ================================================================

    private static void ApplyStdFeedHoleRules(
        FeaturePlanBuilder plan,
        FeedHoleType feedHoleType)
    {
        switch (feedHoleType)
        {
            case FeedHoleType.Std:
                plan.Activate(
                    StdHoleNames);
                break;

            case FeedHoleType.Oval:
                plan.Activate(
                    StdOvalNames);
                break;

            case FeedHoleType.Slot:
                plan.Activate(
                    StdSlotNames);
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the ABT feed-hole type for an FG wedge. " +
                    "Expected STD, Oval or Slot in 'Wed-Feed_H/Slot'.");
        }
    }

    private static void ApplyRevFeedHoleRules(
        FeaturePlanBuilder plan,
        FeedHoleType feedHoleType)
    {
        switch (feedHoleType)
        {
            case FeedHoleType.Std:
                plan.Activate(
                    RevHoleNames);
                break;

            case FeedHoleType.Oval:
                plan.Activate(
                    RevOvalNames);
                break;

            case FeedHoleType.Slot:
                plan.Activate(
                    RevSlotNames);
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the ABT feed-hole type for an FG wedge. " +
                    "Expected STD, Oval or Slot in 'Wed-Feed_H/Slot'.");
        }
    }

    private static FeedHoleType ResolveFeedHoleType(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Feed_H/Slot",
                "Wed_Feed_H_Slot",
                "Wed Feed H Slot",
                "Wed-Feed H Slot",
                "Feed_H/Slot",
                "Feed_H_Slot",
                "Feed H Slot",
                "feed_h_slot");

        var token =
            NormalizeFeedHoleToken(
                raw);

        return token switch
        {
            "STD" => FeedHoleType.Std,
            "OVAL" => FeedHoleType.Oval,
            "SLOT" => FeedHoleType.Slot,
            _ => FeedHoleType.Unknown
        };
    }

    private static string NormalizeFeedHoleToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            RemovePackedDatabaseSuffix(raw)
                .Trim()
                .ToUpperInvariant();

        if (token.StartsWith(
                "STD",
                StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith(
                "STANDARD",
                StringComparison.OrdinalIgnoreCase))
        {
            return "STD";
        }

        if (token.StartsWith(
                "OVAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return "OVAL";
        }

        if (token.StartsWith(
                "SLOT",
                StringComparison.OrdinalIgnoreCase))
        {
            return "SLOT";
        }

        return token;
    }

    // ================================================================
    // FOOT RULES
    // ================================================================

    private static void ApplyStdFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FootOptionType footOption)
    {
        switch (footOption)
        {
            case FootOptionType.C:
                plan.ForceSuppress(
                    "std_round_br");

                ApplyStdCFootRules(
                    plan,
                    facts,
                    withCbr: false);
                break;

            case FootOptionType.CWithCbr:
                plan.ForceSuppress(
                    "std_round_br");

                ApplyStdCFootRules(
                    plan,
                    facts,
                    withCbr: true);
                break;

            case FootOptionType.Vg:
                plan.Activate(
                    StdVgFootNames);
                break;

            case FootOptionType.G:
                plan.Activate(
                    StdGFootNames);
                break;

            case FootOptionType.Cc:
                Logger.Info(
                    "[AbtFeatureRules] STD LW_CC -> no ABT foot " +
                    "features were specified, so all STD foot features remain suppressed.");
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the ABT foot option for an FG wedge. " +
                    "Expected LW_C, LW_C_CBR, LW_VG, LW_G or LW_CC.");
        }
    }

    private static void ApplyRevFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FootOptionType footOption)
    {
        switch (footOption)
        {
            case FootOptionType.C:
                plan.ForceSuppress(
                    "rev_round_br");

                ApplyRevCFootRules(
                    plan,
                    facts,
                    withCbr: false);
                break;

            case FootOptionType.CWithCbr:
                plan.ForceSuppress(
                    "rev_round_br");

                ApplyRevCFootRules(
                    plan,
                    facts,
                    withCbr: true);
                break;

            case FootOptionType.Vg:
                plan.Activate(
                    RevVgFootNames);
                break;

            case FootOptionType.G:
                plan.Activate(
                    RevGFootNames);
                break;

            case FootOptionType.Cc:
                Logger.Info(
                    "[AbtFeatureRules] REV LW_CC -> no ABT foot " +
                    "features were specified, so all REV foot features remain suppressed.");
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the ABT foot option for an FG wedge. " +
                    "Expected LW_C, LW_C_CBR, LW_VG, LW_G or LW_CC.");
        }
    }

    private static void ApplyStdCFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        bool withCbr)
    {
        var froEqualsFr =
            ResolveFroEqualsFr(
                facts);

        plan.Activate(
            StdCFootBaseNames);

        if (withCbr)
        {
            RequireCbrDimensions(
                facts);

            plan.Activate(
                StdCCbrFeature,
                StdCCbrCoreFeature,
                StdCRoundBrFeature);

            if (!froEqualsFr)
            {
                plan.Activate(
                    StdCFrFeature);
            }

            return;
        }

        plan.Activate(
            StdCBrFeature,
            StdCRoundBrFeature);

        if (!froEqualsFr)
        {
            plan.Activate(
                StdCFrFeature);
        }
    }

    private static void ApplyRevCFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        bool withCbr)
    {
        var froEqualsFr =
            ResolveFroEqualsFr(
                facts);

        plan.Activate(
            RevCFootBaseNames);

        if (withCbr)
        {
            RequireCbrDimensions(
                facts);

            plan.Activate(
                RevCCbrFeature,
                RevCCbrCoreFeature,
                RevCRoundBrFeature);

            if (!froEqualsFr)
            {
                plan.Activate(
                    RevCFrFeature);
            }

            return;
        }

        plan.Activate(
            RevCBrFeature,
            RevCRoundBrFeature);

        if (!froEqualsFr)
        {
            plan.Activate(
                RevCFrFeature);
        }
    }

    private static bool ResolveFroEqualsFr(
        WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm(
                "FRO",
                out var froMm))
        {
            throw new InvalidOperationException(
                "Cannot apply the ABT C-foot feature rules because " +
                "dimension 'FRO' is missing or is not a millimeter dimension.");
        }

        if (!facts.TryGetLengthMm(
                "FR",
                out var frMm))
        {
            throw new InvalidOperationException(
                "Cannot apply the ABT C-foot feature rules because " +
                "dimension 'FR' is missing or is not a millimeter dimension.");
        }

        var equal =
            decimal.Abs(
                froMm -
                frMm) <=
            WedgeFacts.DefaultPositiveEpsilon;

        Logger.Info(
            "[AbtFeatureRules] C-foot FRO/FR comparison -> " +
            $"FRO={froMm} mm, FR={frMm} mm, equal={equal}.");

        return equal;
    }

    private static void RequireCbrDimensions(
        WedgeFacts facts)
    {
        if (HasAllPositiveNominal(
                facts,
                "CBRL",
                "CBRD"))
        {
            return;
        }

        throw new InvalidOperationException(
            "ABT foot option C with CBR requires both CBRL and CBRD " +
            "to be greater than zero.");
    }

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Foot_Option",
                "Wed_Foot_Option",
                "Wed Foot Option",
                "Wed-Foot Option",
                "Foot_Option",
                "Foot Option",
                "foot_option");

        var token =
            NormalizePackedToken(
                raw);

        return token switch
        {
            "LW_C" or "SW_C" or "C" =>
                facts.HasPositive("CBR")
                    ? FootOptionType.CWithCbr
                    : FootOptionType.C,
            "LW_VG" or "SW_VG" or "VG" => FootOptionType.Vg,
            "LW_G" or "SW_G" or "G" => FootOptionType.G,
            "LW_CC" or "SW_CC" or "CC" => FootOptionType.Cc,
            _ => FootOptionType.Unknown
        };
    }

    // ================================================================
    // STD OVERLAY
    // ================================================================

    private static void ApplyStdOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FootOptionType footOption,
        bool hasVbl,
        bool hasRa2,
        OverlayVwCase overlayVwCase)
    {
        plan.Deactivate(
            StdOverlayManagedNames);

        ApplyOverlayCutViewRule(
            plan,
            context,
            StdLeftOverlayCutNames,
            StdRightOverlayCutNames);

        ApplyOverlaySubclassWRule(
            plan,
            context.Subclass,
            StdWPgbOverlaySketch,
            StdWFgOverlaySketch);

        ActivateOverlayVwCase(
            plan,
            overlayVwCase,
            StdVwCaseOverlaySketches);

        ActivateOverlayTCase(
            plan,
            hasVbl,
            hasRa2,
            StdTCaseOverlaySketches);

        ApplyOverlayFootRule(
            plan,
            context.Subclass,
            footOption,
            StdFootOverlaySketches);
    }

    // ================================================================
    // REV OVERLAY
    // ================================================================

    private static void ApplyRevOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FootOptionType footOption,
        bool hasVbl,
        bool hasRa2,
        OverlayVwCase overlayVwCase)
    {
        plan.Deactivate(
            RevOverlayManagedNames);

        ApplyOverlayCutViewRule(
            plan,
            context,
            RevLeftOverlayCutNames,
            RevRightOverlayCutNames);

        ApplyOverlaySubclassWRule(
            plan,
            context.Subclass,
            RevWPgbOverlaySketch,
            RevWFgOverlaySketch);

        ActivateOverlayVwCase(
            plan,
            overlayVwCase,
            RevVwCaseOverlaySketches);

        ActivateOverlayTCase(
            plan,
            hasVbl,
            hasRa2,
            RevTCaseOverlaySketches);

        ApplyOverlayFootRule(
            plan,
            context.Subclass,
            footOption,
            RevFootOverlaySketches);
    }

    // ================================================================
    // COMMON OVERLAY RULES
    // ================================================================

    private static void ApplyOverlayCutViewRule(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        string[] leftNames,
        string[] rightNames)
    {
        plan.Deactivate(
            leftNames);

        plan.Deactivate(
            rightNames);

        var overlayView =
            ResolveOverlayViewConfiguration(
                context);

        switch (overlayView)
        {
            case OverlayViewConfiguration.Left:
                plan.Activate(
                    leftNames);

                plan.ForceSuppress(
                    rightNames);

                break;

            case OverlayViewConfiguration.Right:
                plan.Activate(
                    rightNames);

                plan.ForceSuppress(
                    leftNames);

                break;

            case OverlayViewConfiguration.None:
            default:
                plan.ForceSuppress(
                    leftNames);

                plan.ForceSuppress(
                    rightNames);

                break;
        }

        Logger.Info(
            "[AbtFeatureRules] Overlay cut view -> " +
            $"config={context.TargetConfigurationName}, " +
            $"resolved={overlayView}.");
    }

    private static OverlayViewConfiguration ResolveOverlayViewConfiguration(
        FeatureRuleContext context)
    {
        var normalized =
            NormalizePackedToken(
                context.TargetConfigurationName);

        return normalized switch
        {
            "LEFT_VIEW" =>
                OverlayViewConfiguration.Left,

            "RIGHT_VIEW" =>
                OverlayViewConfiguration.Right,

            _ =>
                OverlayViewConfiguration.None
        };
    }

    private static void ApplyOverlaySubclassWRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        string pgbSketch,
        string fgSketch)
    {
        plan.Deactivate(
            pgbSketch,
            fgSketch);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ActivateOnly(
                pgbSketch,
                new[]
                {
                    pgbSketch,
                    fgSketch
                });

            return;
        }

        plan.ActivateOnly(
            fgSketch,
            new[]
            {
                pgbSketch,
                fgSketch
            });
    }

    private static void ActivateOverlayVwCase(
        FeaturePlanBuilder plan,
        OverlayVwCase overlayVwCase,
        string[] vwCaseSketches)
    {
        plan.Deactivate(
            vwCaseSketches);

        if (overlayVwCase == OverlayVwCase.None)
            return;

        plan.ActivateOnly(
            overlayVwCase == OverlayVwCase.Case1
                ? vwCaseSketches[0]
                : vwCaseSketches[1],
            vwCaseSketches);
    }

    private static void ActivateOverlayTCase(
        FeaturePlanBuilder plan,
        bool hasVbl,
        bool hasRa2,
        string[] tCaseSketches)
    {
        plan.Deactivate(
            tCaseSketches);

        var selectedSketch =
            hasVbl
                ? hasRa2
                    ? tCaseSketches[3]
                    : tCaseSketches[1]
                : hasRa2
                    ? tCaseSketches[2]
                    : tCaseSketches[0];

        plan.ActivateOnly(
            selectedSketch,
            tCaseSketches);
    }

    private static void ApplyOverlayFootRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        FootOptionType footOption,
        string[] footOverlaySketches)
    {
        plan.Deactivate(
            footOverlaySketches);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                footOverlaySketches);

            return;
        }

        var selectedSketch =
            footOption switch
            {
                FootOptionType.C or
                FootOptionType.CWithCbr =>
                    footOverlaySketches[0],

                FootOptionType.Vg =>
                    footOverlaySketches[1],

                FootOptionType.G =>
                    footOverlaySketches[2],

                _ =>
                    null
            };

        if (selectedSketch is null)
            return;

        plan.ActivateOnly(
            selectedSketch,
            footOverlaySketches);
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm(
                "VW",
                out var vwMm) ||
            vwMm <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMm))
        {
            Logger.Warn(
                "[AbtFeatureRules] VW is present but W is missing " +
                "or is not a length. No ABT VW overlay case was selected.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMm -
                wMm) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        return OverlayVwCase.Case2;
    }

    // ================================================================
    // SHANK SELECTION
    // ================================================================

    private static AbtShankType ResolveShankType(
        WedgeFacts facts)
    {
        var raw =
            facts.NormalizedPropertyToken(
                "Wed-Type",
                "Wed_Type",
                "Wed Type",
                "Wedge-Type",
                "Wedge_Type",
                "wedge_type");

        var token =
            NormalizePackedToken(
                raw);

        return token switch
        {
            "SW_STD" or "STD" =>
                AbtShankType.Std,

            "SW_180REV" or "SW_180_REV" or "180REV" or "180_REV" =>
                AbtShankType.Rev,

            _ =>
                throw new InvalidOperationException(
                    "Unable to resolve the ABT shank from 'Wed-Type'. " +
                    "Expected SW_STD or SW_180REV, but received " +
                    $"'{DisplayToken(token)}'.")
        };
    }

    // ================================================================
    // SHANK SUPPRESSION
    // ================================================================

    private static void ForceSuppressStdShank(
        FeaturePlanBuilder plan)
    {
        plan.ForceSuppress(StdAlwaysOnNames);
        plan.ForceSuppress(StdVrNames);
        plan.ForceSuppress(StdSlbNames);
        plan.ForceSuppress(StdW2Names);
        plan.ForceSuppress(StdRa2Names);
        plan.ForceSuppress(StdFeedHoleManagedNames);
        plan.ForceSuppress(StdFootManagedNames);
        plan.ForceSuppress(StdOverlayManagedNames);
    }

    private static void ForceSuppressRevShank(
        FeaturePlanBuilder plan)
    {
        plan.ForceSuppress(RevAlwaysOnNames);
        plan.ForceSuppress(RevVrNames);
        plan.ForceSuppress(RevSlbNames);
        plan.ForceSuppress(RevW2Names);
        plan.ForceSuppress(RevRa2Names);
        plan.ForceSuppress(RevFeedHoleManagedNames);
        plan.ForceSuppress(RevFootManagedNames);
        plan.ForceSuppress(RevOverlayManagedNames);
    }

    // ================================================================
    // DIMENSION HELPERS
    // ================================================================

    private static bool HasAllPositiveNominal(
        WedgeFacts facts,
        params string[] dimensionKeys)
    {
        foreach (var key in dimensionKeys)
        {
            if (!facts.HasPositive(key))
                return false;
        }

        return true;
    }

    // ================================================================
    // TOKEN HELPERS
    // ================================================================

    private static string NormalizePackedToken(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            RemovePackedDatabaseSuffix(raw)
                .Trim()
                .Replace('-', '_')
                .Replace(' ', '_')
                .Trim('_')
                .ToUpperInvariant();

        while (token.Contains(
                   "__",
                   StringComparison.Ordinal))
        {
            token =
                token.Replace(
                    "__",
                    "_",
                    StringComparison.Ordinal);
        }

        return token;
    }

    private static string RemovePackedDatabaseSuffix(
        string raw)
    {
        var token =
            raw
                .Trim()
                .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        if (separatorIndex >= 0)
        {
            token =
                token[..separatorIndex];
        }

        return token;
    }

    private static string DisplayToken(
        string token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;
    }

    // ================================================================
    // ENUMS
    // ================================================================

    private enum AbtShankType
    {
        Std,
        Rev
    }

    private enum FeedHoleType
    {
        NotApplicable,
        Unknown,
        Std,
        Oval,
        Slot
    }

    private enum FootOptionType
    {
        NotApplicable,
        Unknown,
        C,
        CWithCbr,
        Vg,
        G,
        Cc
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }

    private enum OverlayViewConfiguration
    {
        None,
        Left,
        Right
    }
}
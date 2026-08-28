using System;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules._4516;

public sealed class _4516FeatureRules : IFeatureRuleSet
{
    // ================================================================
    // ALWAYS-ON FEATURES
    // ================================================================

    private const string TdFeature =
        "td_feature";

    private const string TdSketch =
        "td_sketch";

    private const string IsaFeature =
        "isa_feature";

    private const string IsaSketch =
        "isa_sketch";

    private const string BaFeature =
        "ba_feature";

    private const string BaSketch =
        "ba_sketch";

    private const string NotchFeature =
        "notch_feature";

    private const string NothSketch =
        "noth_sketch";

    // ================================================================
    // CONDITIONAL MAIN FEATURES
    // ================================================================

    private const string VrFeature =
        "vr_feature";

    private const string VrSketch =
        "vr_sketch";

    private const string SlbFeature =
        "slb_feature";

    private const string SlbSketch =
        "slb_sketch";

    // ================================================================
    // FEED-HOLE FEATURES
    // ================================================================

    private const string RoundHoleFeature =
        "round_hole_feature";

    private const string RoundHoleSketch =
        "round_hole_sketch";

    private const string RoundHoleCutFeature =
        "round_hole_cut_feature";

    private const string RoundHoleCutSketch =
        "round_hole_cut_sketch";

    private const string RoundHole =
        "round_hole";

    private const string OvalHolePlan =
        "oval_hole_plan";

    private const string OvalHoleFeature =
        "oval_hole_feature";

    private const string OvalHoleSketch =
        "oval_hole_sketch";

    private const string OvalHoleCutFeature =
        "oval_hole_cut_feature";

    private const string OvalHoleCutSketch =
        "oval_hole_cut_sketch";

    private const string OvalHole =
        "oval_hole";

    private const string SlotHolePlan =
        "slot_hole_plan";

    private const string SlotHoleFeature =
        "slot_hole_feature";

    private const string SlotHoleSketch =
        "slot_hole_sketch";

    private const string SlotHoleCutFeature =
        "slot_hole_cut_feature";

    private const string SlotHoleCutSketch =
        "slot_hole_cut_sketch";

    private const string SlotHole =
        "slot_hole";

    // ================================================================
    // FOOT-OPTION FEATURES
    // ================================================================

    private const string VgFeature =
        "vg_feature";

    private const string VgSketch =
        "vg_sketch";

    private const string VgFrFeature =
        "vg_fr_feature";

    private const string VgBrFeature =
        "vg_br_feature";

    private const string CFeature =
        "c_feature";

    private const string CSketch =
        "c_sketch";

    private const string CFrFeature =
        "c_fr_feature";

    private const string CBrFeature =
        "c_br_feature";

    private const string CCbrFeature =
        "c_cbr_feature";

    private const string GFeature =
        "g_feature";

    private const string GSketch =
        "Sketch21";

    private const string GFrFeature =
        "g_fr_feature";

    private const string GBrFeature =
        "g_br_feature";

    private const string CcFeature =
        "cc_feature";

    private const string CcSketch =
        "cc_sketch";

    private const string FlatBrFeature =
        "flat_br_feature";

    private const string FlatFrFeature =
        "flat_fr_feature";

    // ================================================================
    // OVERLAY CUT FEATURES
    // ================================================================

    private const string RefPoint1 =
        "ref_point_1";

    private const string RefPoint2 =
        "ref_point_2";

    private const string RefPointNonStdCut =
        "ref_point_non_std_cut";

    private const string CutPlanFeature =
        "cut_plan_feature";

    private const string CutFeature =
        "cut_feature";

    private const string NonStdCutPlanFeature =
        "non_std_cut_plan_feature";

    private const string NonStdCutFeature =
        "non_std_cut_feature";

    // ================================================================
    // PGB OVERLAY SKETCHES
    // ================================================================

    private const string WPgbOverlaySketch =
        "w_pgb_overlay_sketch";

    private const string FlPgbOverlaySketch =
        "fl_pgb_overlay_sketch";

    private const string SlbPgbOverlaySketch =
        "slb_pgb_overlay_sketch";

    private const string VwCase1PgbOverlaySketch =
        "vw_case1_pgb_overlay_sketch";

    private const string VwCase2PgbOverlaySketch =
        "vw_case2_pgb_overlay_sketch";

    // ================================================================
    // FG OVERLAY SKETCHES
    // ================================================================

    private const string WFgOverlaySketch =
        "w_fg_overlay_sketch";

    private const string FlFgOverlaySketch =
        "fl_fg_overlay_sketch";

    private const string SlbFgOverlaySketch =
        "slb_fg_overlay_sketch";

    private const string VwCase1FgOverlaySketch =
        "vw_case1_fg_overlay_sketch";

    private const string VwCase2FgOverlaySketch =
        "vw_case2_fg_overlay_sketch";

    private const string VgFgOverlaySketch =
        "vg_fg_overlay_sketch";

    private const string CFgOverlaySketch =
        "c_fg_overlay_sketch";

    private const string GFgOverlaySketch =
        "g_fg_overlay_sketch";

    // ================================================================
    // FEATURE GROUPS
    // ================================================================

    private static readonly string[] AlwaysOnNames =
    {
        TdFeature,
        TdSketch,
        IsaFeature,
        IsaSketch,
        BaFeature,
        BaSketch,
        NotchFeature,
        NothSketch
    };

    private static readonly string[] VrFeatureNames =
    {
        VrFeature,
        VrSketch
    };

    private static readonly string[] SlbFeatureNames =
    {
        SlbFeature,
        SlbSketch
    };

    private static readonly string[] StdFeedHoleNames =
    {
        RoundHoleFeature,
        RoundHoleSketch,
        RoundHoleCutFeature,
        RoundHoleCutSketch,
        RoundHole
    };

    private static readonly string[] OvalFeedHoleNames =
    {
        OvalHolePlan,
        OvalHoleFeature,
        OvalHoleSketch,
        OvalHoleCutFeature,
        OvalHoleCutSketch,
        OvalHole
    };

    private static readonly string[] SlotFeedHoleNames =
    {
        SlotHolePlan,
        SlotHoleFeature,
        SlotHoleSketch,
        SlotHoleCutFeature,
        SlotHoleCutSketch,
        SlotHole
    };

    private static readonly string[] FeedHoleManagedNames =
    {
        RoundHoleFeature,
        RoundHoleSketch,
        RoundHoleCutFeature,
        RoundHoleCutSketch,
        RoundHole,

        OvalHolePlan,
        OvalHoleFeature,
        OvalHoleSketch,
        OvalHoleCutFeature,
        OvalHoleCutSketch,
        OvalHole,

        SlotHolePlan,
        SlotHoleFeature,
        SlotHoleSketch,
        SlotHoleCutFeature,
        SlotHoleCutSketch,
        SlotHole
    };

    private static readonly string[] VgFootNames =
    {
        VgFeature,
        VgSketch,
        VgFrFeature,
        VgBrFeature
    };

    private static readonly string[] CFootNames =
    {
        CFeature,
        CSketch,
        CFrFeature,
        CBrFeature
    };

    private static readonly string[] GFootNames =
    {
        GFeature,
        GSketch,
        GFrFeature,
        GBrFeature
    };

    private static readonly string[] CcFootNames =
    {
        CcFeature,
        CcSketch
    };

    private static readonly string[] FlatFootNames =
    {
        FlatBrFeature,
        FlatFrFeature
    };

    private static readonly string[] FootOptionManagedNames =
    {
        VgFeature,
        VgSketch,
        VgFrFeature,
        VgBrFeature,

        CFeature,
        CSketch,
        CFrFeature,
        CBrFeature,
        CCbrFeature,

        GFeature,
        GSketch,
        GFrFeature,
        GBrFeature,

        CcFeature,
        CcSketch,

        FlatBrFeature,
        FlatFrFeature
    };

    private static readonly string[] OverlayReferenceNames =
    {
        RefPoint1,
        RefPoint2,
        RefPointNonStdCut
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

    private static readonly string[] PgbOverlayManagedNames =
    {
        WPgbOverlaySketch,
        FlPgbOverlaySketch,
        SlbPgbOverlaySketch,
        VwCase1PgbOverlaySketch,
        VwCase2PgbOverlaySketch
    };

    private static readonly string[] FgOverlayManagedNames =
    {
        WFgOverlaySketch,
        FlFgOverlaySketch,
        SlbFgOverlaySketch,
        VwCase1FgOverlaySketch,
        VwCase2FgOverlaySketch,
        VgFgOverlaySketch,
        CFgOverlaySketch,
        GFgOverlaySketch
    };

    private static readonly string[] PgbVwCaseOverlaySketches =
    {
        VwCase1PgbOverlaySketch,
        VwCase2PgbOverlaySketch
    };

    private static readonly string[] FgVwCaseOverlaySketches =
    {
        VwCase1FgOverlaySketch,
        VwCase2FgOverlaySketch
    };

    private static readonly string[] FgFootOverlaySketches =
    {
        VgFgOverlaySketch,
        CFgOverlaySketch,
        GFgOverlaySketch
    };

    private static readonly string[] OverlayManagedNames =
    {
        RefPoint1,
        RefPoint2,
        RefPointNonStdCut,

        CutPlanFeature,
        CutFeature,
        NonStdCutPlanFeature,
        NonStdCutFeature,

        WPgbOverlaySketch,
        FlPgbOverlaySketch,
        SlbPgbOverlaySketch,
        VwCase1PgbOverlaySketch,
        VwCase2PgbOverlaySketch,

        WFgOverlaySketch,
        FlFgOverlaySketch,
        SlbFgOverlaySketch,
        VwCase1FgOverlaySketch,
        VwCase2FgOverlaySketch,
        VgFgOverlaySketch,
        CFgOverlaySketch,
        GFgOverlaySketch
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

        Logger.Info(
            "[_4516FeatureRules] Build -> " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, " +
            $"ruleProfile={context.FeatureRuleProfile ?? "(none)"}.");

        return BuildDrawingPlan(
            facts,
            context);
    }

    // ================================================================
    // DRAWING PLAN
    // ================================================================

    private static ModelRuleRunner.FeaturePlan BuildDrawingPlan(
        WedgeFacts facts,
        FeatureRuleContext context)
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
                "VBLR");

        var hasOverlayVrFamily =
            HasAnyPositiveNominal(
                facts,
                "VR",
                "VRR",
                "VW");

        var overlayVwCase =
            ResolveOverlayVwCase(
                facts,
                hasOverlayVrFamily);

        var feedHoleType =
            ResolveFeedHoleType(
                facts);

        var footOption =
            ResolveFootOption(
                facts);

        /*
         * IMPORTANT:
         *
         * There is no LW_C_CBR / SW_C_CBR foot option.
         *
         * C with CBR is identified only when:
         *
         *     foot option = C
         *     CBRL > 0
         *     CBRD > 0
         */
        var hasCbr =
            HasAllPositiveNominal(
                facts,
                "CBRL",
                "CBRD");

        var plan =
            new FeaturePlanBuilder()
                .Know(AlwaysOnNames)
                .Know(VrFeatureNames)
                .Know(SlbFeatureNames)
                .Know(FeedHoleManagedNames)
                .Know(FootOptionManagedNames)
                .Know(OverlayManagedNames)
                .Activate(AlwaysOnNames)
                .ForceSuppress(
                    SwNames.EngravingFeature,
                    SwNames.EngravingSketch);

        if (hasCompleteVrFamily)
        {
            plan.Activate(
                VrFeatureNames);
        }

        if (hasSlb)
        {
            plan.Activate(
                SlbFeatureNames);
        }

        ApplySubclassFeatureRules(
            plan,
            context.Subclass,
            feedHoleType,
            footOption,
            hasCbr);

        if (isOverlay)
        {
            ApplyOverlayRules(
                plan,
                context,
                footOption,
                hasSlb,
                hasOverlayVrFamily,
                overlayVwCase);
        }
        else
        {
            plan.ForceSuppress(
                OverlayManagedNames);

            Logger.Info(
                "[_4516FeatureRules] Non-overlay drawing -> " +
                "all overlay cut/reference/PGB/FG names suppressed.");
        }

        Logger.Info(
            "[_4516FeatureRules] Drawing plan -> " +
            $"drawingType={context.DrawingType}, " +
            $"subclass={context.Subclass}, " +
            $"feedHole={feedHoleType}, " +
            $"footOption={footOption}, " +
            $"complete VR family={hasCompleteVrFamily}, " +
            $"SLB={hasSlb}, " +
            $"CBR={hasCbr}, " +
            $"overlay VR family={hasOverlayVrFamily}, " +
            $"overlay VW case={overlayVwCase}.");

        return plan.Build();
    }

    // ================================================================
    // SUBCLASS RULES
    // ================================================================

    private static void ApplySubclassFeatureRules(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        FeedHoleType feedHoleType,
        FootOptionType footOption,
        bool hasCbr)
    {
        plan.Deactivate(
            FeedHoleManagedNames);

        plan.Deactivate(
            FootOptionManagedNames);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                FeedHoleManagedNames);

            plan.ForceSuppress(
                FootOptionManagedNames);

            Logger.Info(
                "[_4516FeatureRules] PGB -> all feed-hole and " +
                "foot-option features suppressed.");

            return;
        }

        ApplyFgFeedHoleRules(
            plan,
            feedHoleType);

        ApplyFgFootOptionRules(
            plan,
            footOption,
            hasCbr);
    }

    // ================================================================
    // FEED-HOLE RULES
    // ================================================================

    private static void ApplyFgFeedHoleRules(
        FeaturePlanBuilder plan,
        FeedHoleType feedHoleType)
    {
        switch (feedHoleType)
        {
            case FeedHoleType.Std:
                plan.Activate(
                    StdFeedHoleNames);

                break;

            case FeedHoleType.Oval:
                plan.Activate(
                    OvalFeedHoleNames);

                break;

            case FeedHoleType.Slot:
                plan.Activate(
                    SlotFeedHoleNames);

                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the 4516 feed-hole type. " +
                    "Expected STD(Round), STD, Oval or Slot. " +
                    "The 4516 validation/property-resolution step " +
                    "must run before the feature rules.");
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
            "STD" =>
                FeedHoleType.Std,

            "OVAL" =>
                FeedHoleType.Oval,

            "SLOT" =>
                FeedHoleType.Slot,

            _ =>
                FeedHoleType.Unknown
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
    // FOOT-OPTION RULES
    // ================================================================

    private static void ApplyFgFootOptionRules(
        FeaturePlanBuilder plan,
        FootOptionType footOption,
        bool hasCbr)
    {
        switch (footOption)
        {
            case FootOptionType.Vg:
                plan.Activate(
                    VgFootNames);

                break;

            case FootOptionType.C:
                plan.Activate(
                    CFootNames);

                /*
                 * C with CBR is not a separate foot option.
                 *
                 * If the selected foot is C and both CBRL and CBRD
                 * are positive, activate the CBR feature instead of
                 * the normal C back-radius feature.
                 */
                if (hasCbr)
                {
                    plan.Activate(
                        CCbrFeature);

                    plan.Deactivate(
                        CBrFeature);
                }

                break;

            case FootOptionType.G:
                plan.Activate(
                    GFootNames);

                break;

            case FootOptionType.Cc:
                plan.Activate(
                    CcFootNames);

                break;

            case FootOptionType.Flat:
            default:
                plan.Activate(
                    FlatFootNames);

                break;
        }
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
            NormalizeFootOptionToken(
                raw);

        return token switch
        {
            "LW_VG" or
            "SW_VG" or
            "VG" =>
                FootOptionType.Vg,

            "LW_C" or
            "SW_C" or
            "C" =>
                FootOptionType.C,

            "LW_G" or
            "SW_G" or
            "G" =>
                FootOptionType.G,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                FootOptionType.Cc,

            "LW_FLAT" or
            "SW_FLAT" or
            "FLAT" =>
                FootOptionType.Flat,

            _ =>
                FootOptionType.Flat
        };
    }

    private static string NormalizeFootOptionToken(
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

    // ================================================================
    // OVERLAY RULES
    // ================================================================

    private static void ApplyOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FootOptionType footOption,
        bool hasSlb,
        bool hasOverlayVrFamily,
        OverlayVwCase overlayVwCase)
    {
        plan.Deactivate(
            OverlayManagedNames);

        plan.Activate(
            OverlayReferenceNames);

        ApplyOverlayCutRules(
            plan,
            context);

        ApplyOverlaySketchRules(
            plan,
            context.Subclass,
            footOption,
            hasSlb,
            hasOverlayVrFamily,
            overlayVwCase);
    }

    // ================================================================
    // OVERLAY CUT RULES
    // ================================================================

    private static void ApplyOverlayCutRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context)
    {
        var mode =
            ResolveOverlayCutMode(
                context);

        plan.Deactivate(
            StandardCutNames);

        plan.Deactivate(
            NonStandardCutNames);

        if (mode == OverlayCutMode.NonStandard)
        {
            plan.Activate(
                NonStandardCutNames);
        }
        else
        {
            plan.Activate(
                StandardCutNames);
        }

        Logger.Info(
            "[_4516FeatureRules] Overlay cut selection -> " +
            $"mode={mode}, " +
            $"config={context.TargetConfigurationName}, " +
            $"profile={context.FeatureRuleProfile ?? "(none)"}.");
    }

    private static OverlayCutMode ResolveOverlayCutMode(
        FeatureRuleContext context)
    {
        var token =
            string.IsNullOrWhiteSpace(
                context.FeatureRuleProfile)
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

            "overlay_std_cut" =>
                OverlayCutMode.Standard,

            "overlay" =>
                OverlayCutMode.Standard,

            "default" =>
                OverlayCutMode.Standard,

            OverlayCutProfiles.DefaultConfiguration =>
                OverlayCutMode.Standard,

            OverlayCutProfiles.StandardCut =>
                OverlayCutMode.Standard,

            _ =>
                ResolveUnknownOverlayCutMode(
                    normalized)
        };
    }

    private static OverlayCutMode ResolveUnknownOverlayCutMode(
        string normalizedProfile)
    {
        Logger.Warn(
            "[_4516FeatureRules] Unknown overlay configuration/profile " +
            $"'{normalizedProfile}'. Using the standard cut features.");

        return OverlayCutMode.Standard;
    }

    // ================================================================
    // OVERLAY SKETCH RULES
    // ================================================================

    private static void ApplyOverlaySketchRules(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        FootOptionType footOption,
        bool hasSlb,
        bool hasOverlayVrFamily,
        OverlayVwCase overlayVwCase)
    {
        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                FgOverlayManagedNames);

            plan.Deactivate(
                PgbOverlayManagedNames);

            /*
             * When VR/VW exists, the VW case overlay sketch replaces
             * the standalone W overlay sketch.
             */
            if (hasOverlayVrFamily)
            {
                plan.ForceSuppress(
                    WPgbOverlaySketch);
            }
            else
            {
                plan.Activate(
                    WPgbOverlaySketch);
            }

            plan.Activate(
                FlPgbOverlaySketch);

            if (hasSlb)
            {
                plan.Activate(
                    SlbPgbOverlaySketch);
            }

            ActivatePgbVwCaseSketch(
                plan,
                overlayVwCase);

            Logger.Info(
                "[_4516FeatureRules] PGB overlay -> " +
                $"W overlay={!hasOverlayVrFamily}, " +
                $"VR/VW family={hasOverlayVrFamily}, " +
                "all FG overlay sketches suppressed.");

            return;
        }

        plan.ForceSuppress(
            PgbOverlayManagedNames);

        plan.Deactivate(
            FgOverlayManagedNames);

        /*
         * When VR/VW exists, the VW case overlay sketch replaces
         * the standalone W overlay sketch.
         */
        if (hasOverlayVrFamily)
        {
            plan.ForceSuppress(
                WFgOverlaySketch);
        }
        else
        {
            plan.Activate(
                WFgOverlaySketch);
        }

        plan.Activate(
            FlFgOverlaySketch);

        if (hasSlb)
        {
            plan.Activate(
                SlbFgOverlaySketch);
        }

        ActivateFgVwCaseSketch(
            plan,
            overlayVwCase);

        ActivateFgFootOverlaySketch(
            plan,
            footOption);

        Logger.Info(
            "[_4516FeatureRules] FG overlay -> " +
            $"W overlay={!hasOverlayVrFamily}, " +
            $"VR/VW family={hasOverlayVrFamily}, " +
            "all PGB overlay sketches suppressed.");
    }

    private static void ActivatePgbVwCaseSketch(
        FeaturePlanBuilder plan,
        OverlayVwCase overlayVwCase)
    {
        plan.Deactivate(
            PgbVwCaseOverlaySketches);

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
        plan.Deactivate(
            FgVwCaseOverlaySketches);

        if (overlayVwCase == OverlayVwCase.None)
            return;

        plan.ActivateOnly(
            overlayVwCase == OverlayVwCase.Case1
                ? VwCase1FgOverlaySketch
                : VwCase2FgOverlaySketch,
            FgVwCaseOverlaySketches);
    }

    private static void ActivateFgFootOverlaySketch(
        FeaturePlanBuilder plan,
        FootOptionType footOption)
    {
        plan.Deactivate(
            FgFootOverlaySketches);

        var selectedSketch =
            footOption switch
            {
                FootOptionType.Vg =>
                    VgFgOverlaySketch,

                /*
                 * Normal C and C with CBR use the same
                 * C overlay sketch.
                 */
                FootOptionType.C =>
                    CFgOverlaySketch,

                FootOptionType.G =>
                    GFgOverlaySketch,

                _ =>
                    null
            };

        if (selectedSketch is null)
            return;

        plan.ActivateOnly(
            selectedSketch,
            FgFootOverlaySketches);
    }

    // ================================================================
    // OVERLAY VW CASE
    // ================================================================

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasOverlayVrFamily)
    {
        if (!hasOverlayVrFamily ||
            !facts.TryGetLengthMm(
                "VW",
                out var vwMillimeters) ||
            vwMillimeters <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMillimeters))
        {
            Logger.Warn(
                "[_4516FeatureRules] VW is present but W is missing " +
                "or is not a length. No VW overlay case sketch " +
                "was selected.");

            return OverlayVwCase.None;
        }

        if (decimal.Abs(
                vwMillimeters -
                wMillimeters) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case1;
        }

        if (vwMillimeters >
            wMillimeters +
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.Case2;
        }

        Logger.Warn(
            "[_4516FeatureRules] 4516 overlay received VW < W " +
            $"(VW={vwMillimeters} mm, W={wMillimeters} mm). " +
            "Only VW = W and VW > W are defined. No VW case " +
            "overlay sketch was selected.");

        return OverlayVwCase.None;
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

    // ================================================================
    // TOKEN HELPERS
    // ================================================================

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

    // ================================================================
    // ENUMS
    // ================================================================

    private enum FeedHoleType
    {
        Unknown,
        Std,
        Oval,
        Slot
    }

    private enum FootOptionType
    {
        Flat,
        Vg,
        C,
        G,
        Cc
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
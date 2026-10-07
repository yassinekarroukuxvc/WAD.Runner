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

    // ================================================================
    // CONDITIONAL MAIN FEATURES
    // ================================================================

    private const string NotchFeature =
        "notch_feature";

    // NOTE: the SolidWorks sketch is intentionally named "noth_sketch".
    private const string NothSketch =
        "noth_sketch";

    private const string VrFeature =
        "vr_feature";

    private const string VrSketch =
        "vr_sketch";

    private const string SlbFeature =
        "slb_feature";

    private const string SlbSketch =
        "slb_sketch";

    private const string W2Feature =
        "w2_feature";

    private const string W2Sketch =
        "w2_sketch";

    private const string ErwFeature =
        "erw_feature";

    private const string ErwSketch =
        "erw_sketch";

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

    // VG
    private const string VgFrBrFeature =
        "vg_fr_br_feature";

    private const string VgFrBrSketch =
        "vg_fr_br_sketch";

    private const string VgSketch =
        "vg_sketch";

    private const string VgFrBrCutFeature =
        "vg_fr_br_cut_feature";

    // C
    private const string CFrBrFeature =
        "c_fr_br_feature";

    private const string CFrBrSketch =
        "c_fr_br_sketch";

    private const string CSketch =
        "c_sketch";

    private const string CFrBrCutFeature =
        "c_fr_br_cut_feature";

    // C with CBR
    private const string CFrCbrFeature =
        "c_fr_cbr_feature";

    private const string CFrCbrSketch =
        "c_fr_cbr_sketch";

    private const string CFrCbrCutFeature =
        "c_fr_cbr_cut_feature";

    // G
    private const string GFrBrFeature =
        "g_fr_br_feature";

    private const string GFrBrSketch =
        "g_fr_br_sketch";

    private const string GSketch =
        "g_sketch";

    private const string GFrBrCutFeature =
        "g_fr_br_cut_feature";

    // CG / CC
    private const string CgFeature =
        "cg_feature";

    // NOTE: the sketch under cg_feature is intentionally called cc_sketch.
    private const string CcSketch =
        "cc_sketch";

    // F / flat geometry
    private const string FlatFrBrFeature =
        "flat_fr_br_feature";

    private const string FlatFrBrSketch =
        "flat_fr_br_sketch";

    // ================================================================
    // OVERLAY CUT FEATURES
    // ================================================================

    private const string RefPointRight =
        "ref_point_right";

    private const string RightCutFeature =
        "right_cut_feature";

    private const string RefPointLeft =
        "ref_point_left";

    private const string LeftCutFeature =
        "lef_cut_feature";

    // These are the child sketches used by the overlay cut features.
    private const string RightCutSketch =
        "Sketch1";

    private const string LeftCutSketch =
        "Sketch2";

    // ================================================================
    // OVERLAY SKETCHES
    // ================================================================

    private const string WCase1OverlaySketch =
        "w_case1_overlay_sketch";

    private const string WCase2OverlaySketch =
        "w_case2_overlay_sketch";

    private const string FlCase1OverlaySketch =
        "fl_case1_overlay_sketch";

    private const string FlCase2OverlaySketch =
        "fl_case2_overlay_sketch";

    private const string SlbOverlaySketch =
        "slb_overlay_sketch";

    private const string VwCase1OverlaySketch =
        "vw_case1_overlay_sketch";

    private const string VwCase2OverlaySketch =
        "vw_case2_overlay_sketch";

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
        BaSketch
    };

    private static readonly string[] NotchFeatureNames =
    {
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

    private static readonly string[] W2FeatureNames =
    {
        W2Feature,
        W2Sketch
    };

    private static readonly string[] ErwFeatureNames =
    {
        ErwFeature,
        ErwSketch
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
        VgFrBrFeature,
        VgFrBrSketch,
        VgSketch,
        VgFrBrCutFeature
    };

    private static readonly string[] CFootNames =
    {
        CFrBrFeature,
        CFrBrSketch,
        CSketch,
        CFrBrCutFeature
    };

    private static readonly string[] CCbrFootNames =
    {
        CFrCbrFeature,
        CFrCbrSketch,
        CSketch,
        CFrCbrCutFeature
    };

    private static readonly string[] GFootNames =
    {
        GFrBrFeature,
        GFrBrSketch,
        GSketch,
        GFrBrCutFeature
    };

    private static readonly string[] CgFootNames =
    {
        CgFeature,
        CcSketch
    };

    private static readonly string[] FlatFootNames =
    {
        FlatFrBrFeature,
        FlatFrBrSketch
    };

    private static readonly string[] FootOptionManagedNames =
    {
        VgFrBrFeature,
        VgFrBrSketch,
        VgSketch,
        VgFrBrCutFeature,

        CFrBrFeature,
        CFrBrSketch,
        CSketch,
        CFrBrCutFeature,

        CFrCbrFeature,
        CFrCbrSketch,
        CFrCbrCutFeature,

        GFrBrFeature,
        GFrBrSketch,
        GSketch,
        GFrBrCutFeature,

        CgFeature,
        CcSketch,

        FlatFrBrFeature,
        FlatFrBrSketch
    };

    private static readonly string[] RightOverlayCutNames =
    {
        RefPointRight,
        RightCutFeature,
        RightCutSketch
    };

    private static readonly string[] LeftOverlayCutNames =
    {
        RefPointLeft,
        LeftCutFeature,
        LeftCutSketch
    };

    private static readonly string[] VwCaseOverlaySketches =
    {
        VwCase1OverlaySketch,
        VwCase2OverlaySketch
    };

    private static readonly string[] FgFootOverlaySketches =
    {
        VgFgOverlaySketch,
        CFgOverlaySketch,
        GFgOverlaySketch
    };

    private static readonly string[] OverlaySketchManagedNames =
    {
        WCase1OverlaySketch,
        WCase2OverlaySketch,
        FlCase1OverlaySketch,
        FlCase2OverlaySketch,
        SlbOverlaySketch,
        VwCase1OverlaySketch,
        VwCase2OverlaySketch,
        VgFgOverlaySketch,
        CFgOverlaySketch,
        GFgOverlaySketch
    };

    private static readonly string[] OverlayManagedNames =
    {
        RefPointRight,
        RightCutFeature,
        RightCutSketch,
        RefPointLeft,
        LeftCutFeature,
        LeftCutSketch,
        WCase1OverlaySketch,
        WCase2OverlaySketch,
        FlCase1OverlaySketch,
        FlCase2OverlaySketch,
        SlbOverlaySketch,
        VwCase1OverlaySketch,
        VwCase2OverlaySketch,
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
            new WedgeFacts(wedge, context.Subclass);

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

        // The overlay SLB sketch follows VBL only.
        var hasOverlaySlb =
            facts.HasPositive("VBL");

        // W2 applies to both FG and PGB.
        var hasW2 =
            facts.HasPositive("W2");

        // C drives the PGB notch and the PGB FL overlay case.
        var hasPositiveC =
            facts.HasPositive("C");

        // FG ERW requires the full ERW dimension family.
        var hasFgErw =
            context.Subclass == WedgeSubclass.FG &&
            HasAllPositiveNominal(
                facts,
                "ERD",
                "ERL",
                "FLER",
                "CA",
                "ERW");

        // Overlay VW case logic is active only when BOTH VR and VW are present.
        var hasOverlayVrFamily =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW");

        var overlayVwCase =
            ResolveOverlayVwCase(
                facts,
                hasOverlayVrFamily);

        var feedHoleType =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFeedHoleType(facts)
                : FeedHoleType.NotApplicable;

        var footOption =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFootOption(facts)
                : FootOptionType.NotApplicable;

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
            context.Subclass == WedgeSubclass.FG &&
            HasAllPositiveNominal(
                facts,
                "CBRL",
                "CBRD");

        var plan =
            new FeaturePlanBuilder()
                .Know(AlwaysOnNames)
                .Know(NotchFeatureNames)
                .Know(VrFeatureNames)
                .Know(SlbFeatureNames)
                .Know(W2FeatureNames)
                .Know(ErwFeatureNames)
                .Know(FeedHoleManagedNames)
                .Know(FootOptionManagedNames)
                .Know(OverlayManagedNames)
                .Activate(AlwaysOnNames)
                .Deactivate(NotchFeatureNames)
                .Deactivate(W2FeatureNames)
                .Deactivate(ErwFeatureNames)
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

        // FG keeps the previous always-on notch behavior.
        // PGB now activates notch only when C > 0.
        if (context.Subclass == WedgeSubclass.FG ||
            (context.Subclass == WedgeSubclass.PGB && hasPositiveC))
        {
            plan.Activate(
                NotchFeatureNames);
        }

        // W2 applies to both subclasses.
        if (hasW2)
        {
            plan.Activate(
                W2FeatureNames);
        }

        // ERW applies to FG only and requires all five dimensions.
        if (hasFgErw)
        {
            plan.Activate(
                ErwFeatureNames);
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
                hasOverlaySlb,
                hasOverlayVrFamily,
                overlayVwCase,
                hasPositiveC);
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
            $"overlay SLB={hasOverlaySlb}, " +
            $"W2={hasW2}, " +
            $"C>0={hasPositiveC}, " +
            $"FG ERW={hasFgErw}, " +
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
                // Normal C geometry is always active for C.
                plan.Activate(
                    CFootNames);

                // C with CBR adds the CBR branch when both CBR values are > 0.
                if (hasCbr)
                {
                    plan.Activate(
                        CCbrFootNames);
                }

                break;

            case FootOptionType.G:
                plan.Activate(
                    GFootNames);

                break;

            case FootOptionType.Cg:
                plan.Activate(
                    CgFootNames);

                break;

            case FootOptionType.Cc:
                // CC is valid only with both CBR dimensions.
                if (hasCbr)
                {
                    plan.Activate(
                        CCbrFootNames);

                    plan.Activate(
                        CgFootNames);
                }
                else
                {
                    Logger.Warn(
                        "[_4516FeatureRules] CC selected without both CBRL and CBRD > 0. " +
                        "CC geometry remains suppressed; validation should reject this input.");
                }

                break;

            case FootOptionType.Flat:
            default:
                // Empty, unsupported, F, LW_F and SW_F all use flat geometry.
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

            "LW_CG" or
            "SW_CG" or
            "CG" =>
                FootOptionType.Cg,

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                FootOptionType.Cc,

            "LW_F" or
            "SW_F" or
            "F" =>
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
        bool hasOverlaySlb,
        bool hasOverlayVrFamily,
        OverlayVwCase overlayVwCase,
        bool hasPositiveC)
    {
        plan.Deactivate(
            OverlayManagedNames);

        ApplyOverlayCutViewRule(
            plan,
            context.TargetConfigurationName);

        ApplyOverlaySketchRules(
            plan,
            context.Subclass,
            footOption,
            hasOverlaySlb,
            hasOverlayVrFamily,
            overlayVwCase,
            hasPositiveC);
    }

    // ================================================================
    // OVERLAY CUT RULES
    // ================================================================

    private static void ApplyOverlayCutViewRule(
        FeaturePlanBuilder plan,
        string? configurationName)
    {
        var overlayView =
            ResolveOverlayViewConfiguration(
                configurationName);

        switch (overlayView)
        {
            case OverlayViewConfiguration.Left:
                plan.Activate(
                    LeftOverlayCutNames);

                plan.ForceSuppress(
                    RightOverlayCutNames);
                break;

            case OverlayViewConfiguration.Right:
                plan.Activate(
                    RightOverlayCutNames);

                plan.ForceSuppress(
                    LeftOverlayCutNames);
                break;

            default:
                plan.ForceSuppress(
                    LeftOverlayCutNames);

                plan.ForceSuppress(
                    RightOverlayCutNames);
                break;
        }

        var activeCutSketch =
            overlayView switch
            {
                OverlayViewConfiguration.Right => RightCutSketch,
                OverlayViewConfiguration.Left => LeftCutSketch,
                _ => "(none)"
            };

        Logger.Info(
            "[_4516FeatureRules] Overlay cut selection -> " +
            $"view={overlayView}, " +
            $"config={configurationName ?? "(none)"}, " +
            $"cutSketch={activeCutSketch}.");
    }

    private static OverlayViewConfiguration ResolveOverlayViewConfiguration(
        string? configurationName)
    {
        return NormalizeFootOptionToken(
            configurationName) switch
        {
            "LEFT_VIEW" =>
                OverlayViewConfiguration.Left,

            "RIGHT_VIEW" =>
                OverlayViewConfiguration.Right,

            _ =>
                OverlayViewConfiguration.None
        };
    }

    // ================================================================
    // OVERLAY SKETCH RULES
    // ================================================================

    private static void ApplyOverlaySketchRules(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        FootOptionType footOption,
        bool hasOverlaySlb,
        bool hasOverlayVrFamily,
        OverlayVwCase overlayVwCase,
        bool hasPositiveC)
    {
        plan.Deactivate(
            OverlaySketchManagedNames);

        if (subclass == WedgeSubclass.PGB)
        {
            // PGB always uses W case 1.
            plan.Activate(
                WCase1OverlaySketch);

            // PGB FL selection is driven by C.
            plan.Activate(
                hasPositiveC
                    ? FlCase1OverlaySketch
                    : FlCase2OverlaySketch);

            if (hasOverlaySlb)
            {
                plan.Activate(
                    SlbOverlaySketch);
            }

            ActivateVwCaseSketch(
                plan,
                hasOverlayVrFamily,
                overlayVwCase);

            Logger.Info(
                "[_4516FeatureRules] PGB overlay sketches -> " +
                $"W={WCase1OverlaySketch}, " +
                $"FL={(hasPositiveC ? FlCase1OverlaySketch : FlCase2OverlaySketch)}, " +
                $"SLB={hasOverlaySlb}, " +
                $"VR/VW={hasOverlayVrFamily}, case={overlayVwCase}.");

            return;
        }

        // FG always uses W case 2 and FL case 1.
        plan.Activate(
            WCase2OverlaySketch,
            FlCase1OverlaySketch);

        if (hasOverlaySlb)
        {
            plan.Activate(
                SlbOverlaySketch);
        }

        ActivateVwCaseSketch(
            plan,
            hasOverlayVrFamily,
            overlayVwCase);

        ActivateFgFootOverlaySketch(
            plan,
            footOption);

        Logger.Info(
            "[_4516FeatureRules] FG overlay sketches -> " +
            $"W={WCase2OverlaySketch}, FL={FlCase1OverlaySketch}, " +
            $"SLB={hasOverlaySlb}, " +
            $"VR/VW={hasOverlayVrFamily}, case={overlayVwCase}, " +
            $"foot={footOption}.");
    }

    private static void ActivateVwCaseSketch(
        FeaturePlanBuilder plan,
        bool hasOverlayVrFamily,
        OverlayVwCase overlayVwCase)
    {
        plan.Deactivate(
            VwCaseOverlaySketches);

        if (!hasOverlayVrFamily ||
            overlayVwCase == OverlayVwCase.None)
        {
            return;
        }

        plan.ActivateOnly(
            overlayVwCase == OverlayVwCase.Case1
                ? VwCase1OverlaySketch
                : VwCase2OverlaySketch,
            VwCaseOverlaySketches);
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

                // Normal C and C-with-CBR share the C overlay sketch.
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
        NotApplicable,
        Unknown,
        Std,
        Oval,
        Slot
    }

    private enum FootOptionType
    {
        NotApplicable,
        Flat,
        Vg,
        C,
        G,
        Cg,
        Cc
    }

    private enum OverlayViewConfiguration
    {
        None,
        Left,
        Right
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
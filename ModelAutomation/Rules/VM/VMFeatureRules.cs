using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.VM;

/// <summary>
/// Feature rules for the VM wedge type.
///
/// VM has one model family:
///     std_shank
///
/// PGB:
///     Uses the std_pgb feature family.
///     PGB has no feed-hole or foot-option property.
///     For non-overlay PGB drawings, everything outside the std_pgb
///     family is suppressed.
///
/// FG:
///     Feed-hole property: Wed-Feed_H/Slot.
///     Foot-option property: Wed-Foot_Option.
///
/// CBR:
///     There is no separate CBR foot-option token.
///     A C foot becomes C + CBR when CBRL > 0 and CBRD > 0.
///
/// Overlay:
///     left_view  -> left cut/reference family.
///     right_view -> right cut/reference family.
///
///     The subclass W overlay sketch is always active:
///         PGB -> std_w_pgb_overlay_sketch
///         FG  -> std_w_fg_overlay_sketch
///
///     VW case:
///         Case 1 -> VR > 0, VW > 0, VW = W
///         Case 2 -> VR > 0, VW > 0, VW != W
///
///     T case:
///         Case 1 -> VBL <= 0, RA2 <= 0
///         Case 2 -> VBL > 0,  RA2 <= 0
///         Case 3 -> VBL <= 0, RA2 > 0
///         Case 4 -> VBL > 0,  RA2 > 0
/// </summary>
public sealed class VMFeatureRules : IFeatureRuleSet
{
    /*
     * Features that belong to std_pgb.
     *
     * These are the only normal production/customer features that PGB
     * is allowed to use.
     */
    private static readonly string[] PgbAlwaysOn =
    {
        "td_std_feature",
        "td_std_sketch",

        "isa_std_feature",
        "isa_std_sketch",

        "ba_std_feature",
        "ba_std_sketch",

        "nd_std_feature",
        "nd_std_sketch"
    };

    private static readonly string[] Vr =
    {
        "vr_std_feature",
        "vr_std_sketch"
    };

    private static readonly string[] Slb =
    {
        "slb_std_feature",
        "slb_std_sketch"
    };

    /*
     * FG-only base features.
     */
    private static readonly string[] FgAlwaysOn =
    {
        "hole_std_feature",
        "Sketch1",
        "Sketch2",
        "nr_feature"
    };

    private static readonly string[] W2 =
    {
        "w2_std_feature",
        "w2_std_sketch"
    };

    private static readonly string[] Ra2 =
    {
        "ra2_std_feature",
        "ra2_std_sketch"
    };

    private static readonly string[] Fro =
    {
        "fro_std_feature",
        "fro_std_sketch"
    };

    private static readonly string[] Tip =
    {
        "tip_feature",
        "tip_sketch"
    };

    /*
     * Feed-hole families.
     */
    private static readonly string[] StdHole =
    {
        "std_hole_feature",
        "std_hole_sketch",
        "std_hole_cut_feature",
        "std_hole_cut_sketch",
        "std_hole_combine"
    };

    private static readonly string[] OvalHole =
    {
        "std_oval_plan",
        "std_oval_feature",
        "std_oval_sketch",
        "std_oval_cut_feature",
        "std_oval_cut_sketch",
        "std_oval_combine"
    };

    private static readonly string[] SlotHole =
    {
        "std_slot_plan",
        "std_slot_feature",
        "std_slot_sketch",
        "std_slot_cut_feature",
        "std_slot_cut_sketch",
        "std_slot_combine"
    };

    /*
     * Foot-option families.
     */
    private static readonly string[] CBase =
    {
        "std_c_feature",
        "std_c_sketch"
    };

    /*
     * The feature tree names this feature std_fr_c_feature.
     *
     * The explanatory text used std_c_fr_feature in one place.
     * The tree name is used here because it is the concrete feature-tree name
     * and matches the naming style used by the existing 1001 implementation.
     */
    private const string CFr =
        "std_fr_c_feature";

    private const string CBr =
        "std_br_c_feature";

    private const string CCbr =
        "std_cbr_c_feature";

    /*
     * Explicitly required by the VM C+CBR rule.
     */
    private const string CCbrCore =
        "std_cbr_c_core_feature";

    private static readonly string[] Vg =
    {
        "std_vg_feature",
        "std_vg_sketch",
        "std_fr_vg_feature",
        "std_br_vg_feature"
    };

    private static readonly string[] G =
    {
        "std_g_feature",
        "std_g_sketch",
        "std_g_fr_feature",
        "std_g_br_feature"
    };

    private static readonly string[] F =
    {
        "std_fr_f_feature",
        "std_br_f_feature"
    };

    /*
     * Overlay cut/reference families.
     */
    private static readonly string[] RightCut =
    {
        "std_ref_point_right",
        "std_right_cut_plan",
        "std_right_cut"
    };

    private static readonly string[] LeftCut =
    {
        "std_ref_point_left",
        "std_left_cut_plan",
        "std_left_cut"
    };

    /*
     * Overlay sketches.
     */
    private const string WPgbOverlay =
        "std_w_pgb_overlay_sketch";

    private const string WFgOverlay =
        "std_w_fg_overlay_sketch";

    private static readonly string[] VwCases =
    {
        "std_vw_case1_overlay_sketch",
        "std_vw_case2_overlay_sketch"
    };

    private static readonly string[] TCases =
    {
        "std_t_case1_overlay_sketch",
        "std_t_case2_overlay_sketch",
        "std_t_case3_overlay_sketch",
        "std_t_case4_overlay_sketch"
    };

    private static readonly string[] FootOverlays =
    {
        "std_c_overlay_sketch",
        "std_vg_overlay_sketch",
        "std_g_overlay_sketch"
    };

    private static readonly string[] FeedHoleManaged =
        StdHole
            .Concat(OvalHole)
            .Concat(SlotHole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] FootManaged =
        CBase
            .Concat(
                new[]
                {
                    CFr,
                    CBr,
                    CCbr,
                    CCbrCore
                })
            .Concat(Vg)
            .Concat(G)
            .Concat(F)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] FgOnlyManaged =
        FgAlwaysOn
            .Concat(W2)
            .Concat(Ra2)
            .Concat(Fro)
            .Concat(Tip)
            .Concat(FeedHoleManaged)
            .Concat(FootManaged)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] OverlayManaged =
        RightCut
            .Concat(LeftCut)
            .Concat(
                new[]
                {
                    WPgbOverlay,
                    WFgOverlay
                })
            .Concat(VwCases)
            .Concat(TCases)
            .Concat(FootOverlays)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] AllManaged =
        PgbAlwaysOn
            .Concat(Vr)
            .Concat(Slb)
            .Concat(FgOnlyManaged)
            .Concat(OverlayManaged)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ModelRuleRunner.FeaturePlan Build(
        WedgeData wedge,
        FeatureRuleContext context)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var facts =
            new WedgeFacts(
                wedge,
                context.Subclass);

        /*
         * std_pgb conditional families.
         *
         * VM VR rule:
         *     VR > 0 AND VW > 0 AND VRR > 0
         *
         * VM SLB rule:
         *     VBL > 0 AND VBLR > 0
         */
        var hasVr =
            HasAllPositive(
                facts,
                "VR",
                "VW",
                "VRR");

        var hasSlb =
            HasAllPositive(
                facts,
                "VBL",
                "VBLR");

        /*
         * FG-only conditional features.
         */
        var hasW2 =
            facts.HasPositive(
                "W2");

        var hasRa2 =
            HasAllPositive(
                facts,
                "RA2",
                "RA2H");

        var hasFro =
            facts.HasPositive(
                "FRO");

        var hasTip =
            facts.HasPositive(
                "TIP");

        /*
         * Overlay case inputs.
         *
         * VW cases are based specifically on VR > 0 AND VW > 0.
         */
        var hasOverlayVrVw =
            HasAllPositive(
                facts,
                "VR",
                "VW");

        var hasOverlayVbl =
            facts.HasPositive(
                "VBL");

        var hasOverlayRa2 =
            facts.HasPositive(
                "RA2");

        var vwCase =
            ResolveOverlayVwCase(
                facts,
                hasOverlayVrVw);

        var feedHole =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFeedHoleType(facts)
                : FeedHoleType.NotApplicable;

        var footOption =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFootOption(facts)
                : FootOptionType.NotApplicable;

        var hasCbr =
            context.Subclass == WedgeSubclass.FG &&
            footOption == FootOptionType.C &&
            HasAllPositive(
                facts,
                "CBRL",
                "CBRD");

        var plan =
            new FeaturePlanBuilder()
                .Know(AllManaged)
                .ForceSuppress(
                    SwNames.EngravingFeature,
                    SwNames.EngravingSketch);

        /*
         * std_pgb is shared by FG and PGB.
         */
        ApplyPgbFamilyRules(
            plan,
            hasVr,
            hasSlb);

        /*
         * Everything outside std_pgb is subclass-dependent.
         */
        ApplySubclassRules(
            plan,
            facts,
            context.Subclass,
            feedHole,
            footOption,
            hasW2,
            hasRa2,
            hasFro,
            hasTip);

        if (context.DrawingType == DrawingType.Overlay)
        {
            ApplyOverlayRules(
                plan,
                context,
                footOption,
                hasOverlayVbl,
                hasOverlayRa2,
                hasOverlayVrVw,
                vwCase);
        }
        else
        {
            /*
             * Production / Customer:
             * no overlay geometry.
             *
             * For PGB this means only std_pgb remains active.
             */
            plan.ForceSuppress(
                OverlayManaged);
        }

        Logger.Info(
            "[VMFeatureRules] Build -> " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, " +
            $"feedHole={feedHole}, " +
            $"footOption={footOption}, " +
            $"CBR={hasCbr}, " +
            $"VR={hasVr}, " +
            $"SLB={hasSlb}, " +
            $"W2={hasW2}, " +
            $"RA2={hasRa2}, " +
            $"FRO={hasFro}, " +
            $"TIP={hasTip}, " +
            $"overlayVR/VW={hasOverlayVrVw}, " +
            $"VW case={vwCase}.");

        return plan.Build();
    }

    private static void ApplyPgbFamilyRules(
        FeaturePlanBuilder plan,
        bool hasVr,
        bool hasSlb)
    {
        /*
         * Always ON inside std_pgb:
         *     TD
         *     ISA
         *     BA
         *     ND
         */
        plan.Activate(
            PgbAlwaysOn);

        if (hasVr)
        {
            plan.Activate(
                Vr);
        }
        else
        {
            plan.Deactivate(
                Vr);
        }

        if (hasSlb)
        {
            plan.Activate(
                Slb);
        }
        else
        {
            plan.Deactivate(
                Slb);
        }
    }

    private static void ApplySubclassRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        WedgeSubclass subclass,
        FeedHoleType feedHole,
        FootOptionType footOption,
        bool hasW2,
        bool hasRa2,
        bool hasFro,
        bool hasTip)
    {
        /*
         * Start with every FG-only feature off.
         */
        plan.Deactivate(
            FgOnlyManaged);

        if (subclass == WedgeSubclass.PGB)
        {
            /*
             * PGB has no feed-hole or foot option.
             *
             * For Production / Customer this leaves only std_pgb.
             * Overlay-specific geometry is handled later when the
             * drawing type is Overlay.
             */
            plan.ForceSuppress(
                FgOnlyManaged);

            return;
        }

        if (subclass != WedgeSubclass.FG)
        {
            throw new InvalidOperationException(
                $"VM feature rules support FG and PGB only, but received '{subclass}'.");
        }

        /*
         * FG always ON:
         *
         * hole_std_feature
         * Sketch1
         * Sketch2
         * nr_feature
         */
        plan.Activate(
            FgAlwaysOn);

        if (hasW2)
        {
            plan.Activate(
                W2);
        }

        if (hasRa2)
        {
            plan.Activate(
                Ra2);
        }

        if (hasFro)
        {
            plan.Activate(
                Fro);
        }

        if (hasTip)
        {
            plan.Activate(
                Tip);
        }

        ApplyFeedHoleRules(
            plan,
            facts,
            feedHole);

        ApplyFootRules(
            plan,
            facts,
            footOption);
    }

    private static void ApplyFeedHoleRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FeedHoleType feedHole)
    {
        plan.Deactivate(
            FeedHoleManaged);

        switch (feedHole)
        {
            case FeedHoleType.Std:
                plan.Activate(
                    StdHole);
                break;

            case FeedHoleType.Oval:
                plan.Activate(
                    OvalHole);
                break;

            case FeedHoleType.Slot:
                plan.Activate(
                    SlotHole);
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the VM feed-hole type for an FG wedge. " +
                    $"Expected STD, Oval or Slot in " +
                    $"'{facts.EffectivePropertyName("Wed-Feed_H/Slot")}'.");
        }
    }

    private static void ApplyFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FootOptionType footOption)
    {
        plan.Deactivate(
            FootManaged);

        switch (footOption)
        {
            case FootOptionType.C:
                ApplyCFootRules(
                    plan,
                    facts);
                break;

            case FootOptionType.Vg:
                plan.Activate(
                    Vg);
                break;

            case FootOptionType.G:
                plan.Activate(
                    G);
                break;

            case FootOptionType.F:
                /*
                 * F:
                 *     std_fr_f_feature ON
                 *     std_br_f_feature ON
                 *
                 * FRO must be OFF even when FRO > 0.
                 */
                plan.Activate(
                    F);

                plan.ForceSuppress(
                    Fro);
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the VM foot option for an FG wedge. " +
                    "Expected LW_/SW_ C, VG, G or F.");
        }
    }

    private static void ApplyCFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts)
    {
        var froEqualsFr =
            ResolveFroEqualsFr(
                facts);

        var hasCbr =
            HasAllPositive(
                facts,
                "CBRL",
                "CBRD");

        /*
         * Every C foot uses the C base.
         */
        plan.Activate(
            CBase);

        if (hasCbr)
        {
            /*
             * C with CBR:
             *
             * CBRL > 0 AND CBRD > 0.
             *
             * The normal C back-radius feature is not used.
             */
            plan.Activate(
                CCbr);

            plan.ForceSuppress(
                CBr);

            if (froEqualsFr)
            {
                /*
                 * FRO = FR:
                 *     std_cbr_c_feature
                 *     std_cbr_c_core_feature
                 */
                plan.Activate(
                    CCbrCore);

                plan.ForceSuppress(
                    CFr);
            }
            else
            {
                /*
                 * FRO != FR:
                 *     std_cbr_c_feature
                 *     std_fr_c_feature
                 */
                plan.Activate(
                    CFr);

                plan.ForceSuppress(
                    CCbrCore);
            }

            return;
        }

        /*
         * Normal C:
         *
         * std_c_feature / std_c_sketch
         * std_br_c_feature
         *
         * If FRO != FR, also enable the C front-radius feature.
         */
        plan.Activate(
            CBr);

        plan.ForceSuppress(
            CCbr,
            CCbrCore);

        if (froEqualsFr)
        {
            plan.ForceSuppress(
                CFr);
        }
        else
        {
            plan.Activate(
                CFr);
        }
    }

    private static void ApplyOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FootOptionType footOption,
        bool hasVbl,
        bool hasRa2,
        bool hasOverlayVrVw,
        OverlayVwCase vwCase)
    {
        /*
         * Reset every overlay-controlled feature first.
         */
        plan.Deactivate(
            OverlayManaged);

        ApplyOverlayCutRule(
            plan,
            context);

        /*
         * VM W overlay behavior:
         *
         * PGB Overlay:
         *     std_w_pgb_overlay_sketch ALWAYS ON
         *
         * FG Overlay:
         *     std_w_fg_overlay_sketch ALWAYS ON
         *
         * Unlike 1001, activating a VW case does NOT suppress
         * the subclass W overlay sketch.
         */
        if (context.Subclass == WedgeSubclass.PGB)
        {
            plan.Activate(
                WPgbOverlay);

            plan.ForceSuppress(
                WFgOverlay);
        }
        else if (context.Subclass == WedgeSubclass.FG)
        {
            plan.Activate(
                WFgOverlay);

            plan.ForceSuppress(
                WPgbOverlay);
        }

        /*
         * VW overlay cases:
         *
         * Case 1 -> VR > 0, VW > 0, VW = W
         * Case 2 -> VR > 0, VW > 0, VW != W
         */
        if (hasOverlayVrVw &&
            vwCase != OverlayVwCase.None)
        {
            plan.ActivateOnly(
                vwCase == OverlayVwCase.Case1
                    ? VwCases[0]
                    : VwCases[1],
                VwCases);
        }
        else
        {
            plan.ForceSuppress(
                VwCases);
        }

        /*
         * T overlay:
         *
         * Case 1 = normal case
         * Case 2 = VBL > 0
         * Case 3 = RA2 > 0
         * Case 4 = VBL > 0 AND RA2 > 0
         */
        var tSketch =
            hasVbl
                ? hasRa2
                    ? TCases[3]
                    : TCases[1]
                : hasRa2
                    ? TCases[2]
                    : TCases[0];

        plan.ActivateOnly(
            tSketch,
            TCases);

        /*
         * PGB has no foot option.
         */
        if (context.Subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                FootOverlays);

            Logger.Info(
                "[VMFeatureRules] PGB overlay -> " +
                $"VW family={hasOverlayVrVw}, " +
                $"VW case={vwCase}, " +
                $"T sketch={tSketch}.");

            return;
        }

        /*
         * FG foot overlay:
         *
         * C and C+CBR -> C overlay
         * VG          -> VG overlay
         * G           -> G overlay
         * F           -> no foot overlay sketch
         */
        var footSketch =
            footOption switch
            {
                FootOptionType.C =>
                    FootOverlays[0],

                FootOptionType.Vg =>
                    FootOverlays[1],

                FootOptionType.G =>
                    FootOverlays[2],

                FootOptionType.F =>
                    null,

                _ =>
                    null
            };

        if (footSketch is not null)
        {
            plan.ActivateOnly(
                footSketch,
                FootOverlays);
        }
        else
        {
            plan.ForceSuppress(
                FootOverlays);
        }

        Logger.Info(
            "[VMFeatureRules] FG overlay -> " +
            $"VW family={hasOverlayVrVw}, " +
            $"VW case={vwCase}, " +
            $"T sketch={tSketch}, " +
            $"foot={footOption}.");
    }

    private static void ApplyOverlayCutRule(
        FeaturePlanBuilder plan,
        FeatureRuleContext context)
    {
        var view =
            NormalizePackedToken(
                context.TargetConfigurationName);

        switch (view)
        {
            case "LEFT_VIEW":
                plan.Activate(
                    LeftCut);

                plan.ForceSuppress(
                    RightCut);
                break;

            case "RIGHT_VIEW":
                plan.Activate(
                    RightCut);

                plan.ForceSuppress(
                    LeftCut);
                break;

            default:
                plan.ForceSuppress(
                    LeftCut);

                plan.ForceSuppress(
                    RightCut);
                break;
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

    private static FootOptionType ResolveFootOption(
        WedgeFacts facts)
    {
        var token =
            NormalizePackedToken(
                facts.NormalizedPropertyToken(
                    "Wed-Foot_Option",
                    "Wed_Foot_Option",
                    "Wed Foot Option",
                    "Wed-Foot Option",
                    "Foot_Option",
                    "Foot Option",
                    "foot_option"));

        /*
         * C with CBR does not have a separate database token.
         *
         * LW_C / SW_C / C remains FootOptionType.C.
         * CBRL + CBRD determine whether the CBR geometry is used.
         */
        return token switch
        {
            "LW_C" or
            "SW_C" or
            "C" =>
                FootOptionType.C,

            "LW_VG" or
            "SW_VG" or
            "VG" =>
                FootOptionType.Vg,

            "LW_G" or
            "SW_G" or
            "G" =>
                FootOptionType.G,

            "LW_F" or
            "SW_F" or
            "F" =>
                FootOptionType.F,

            _ =>
                FootOptionType.Unknown
        };
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasOverlayVrVw)
    {
        if (!hasOverlayVrVw)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vw) ||
            vw <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return OverlayVwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var w))
        {
            Logger.Warn(
                "[VMFeatureRules] VW is present but W is missing/not a length. " +
                "No VW overlay case selected.");

            return OverlayVwCase.None;
        }

        return decimal.Abs(
                   vw -
                   w) <=
               WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVwCase.Case1
            : OverlayVwCase.Case2;
    }

    private static bool ResolveFroEqualsFr(
        WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm(
                "FRO",
                out var fro))
        {
            throw new InvalidOperationException(
                "Cannot apply VM C-foot rules because FRO is missing/not a length.");
        }

        if (!facts.TryGetLengthMm(
                "FR",
                out var fr))
        {
            throw new InvalidOperationException(
                "Cannot apply VM C-foot rules because FR is missing/not a length.");
        }

        return decimal.Abs(
                   fro -
                   fr) <=
               WedgeFacts.DefaultPositiveEpsilon;
    }

    private static bool HasAllPositive(
        WedgeFacts facts,
        params string[] keys)
        => keys.All(
            key => facts.HasPositive(key));

    private static string NormalizeFeedHoleToken(
        string? raw)
    {
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

    private static string NormalizePackedToken(
        string? raw)
    {
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
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token =
            raw
                .Trim()
                .Trim('\0');

        var separatorIndex =
            token.IndexOf(';');

        return separatorIndex >= 0
            ? token[..separatorIndex]
            : token;
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
        Vg,
        G,
        F
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}

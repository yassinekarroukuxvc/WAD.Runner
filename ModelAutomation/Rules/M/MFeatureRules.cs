using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.M;

/// <summary>
/// Feature rules for the M wedge type.
///
/// Wed-Type:
///     SW_STD    -> std_* feature family
///     SW_180REV -> rev_* feature family
///
/// PGB:
///     Feed-hole and foot-option features are suppressed.
///
/// FG:
///     Feed-hole features are selected from Wed-Feed_H/Slot.
///     Foot features are selected from Wed-Foot_Option.
///
/// CBR:
///     There is no separate CBR foot-option token.
///     A normal C foot becomes C+CBR when both CBRL and CBRD are > 0.
///
/// F foot:
///     STD -> std_f_fr_feature + std_f_br_feature ON,
///            FRO feature/sketch OFF.
///
///     REV -> rev_f_fr_feature + rev_f_br_feature ON,
///            FRO feature OFF.
///
///     F does not have a foot-option overlay sketch.
///
/// Overlay:
///     left_view  -> left cut/reference family
///     right_view -> right cut/reference family.
///
///     When the VR/VW family is present:
///         W overlay sketch -> OFF
///         VW case sketch   -> ON
///
///     When the VR/VW family is absent:
///         subclass-specific W overlay sketch -> ON
/// </summary>
public sealed class MFeatureRules : IFeatureRuleSet
{
    private static readonly FeatureFamily Std = new(
        AlwaysOn: new[]
        {
            "td_std_feature", "td_std_sketch",
            "isa_std_feature", "isa_std_sketch",
            "ba_std_feature", "ba_std_sketch",
            "nd_std_feature", "nd_std_sketch",
            "fro_std_feature", "fro_std_sketch"
        },
        Fro: new[]
        {
            "fro_std_feature",
            "fro_std_sketch"
        },
        Vr: new[]
        {
            "vr_std_feature",
            "vr_std_sketch"
        },
        Slb: new[]
        {
            "slb_std_feature",
            "slb_std_sketch"
        },
        W2: new[]
        {
            "w2_std_feature",
            "w2_std_sketch"
        },
        Ra2: new[]
        {
            "ra2_std_feature",
            "ra2_std_sketch"
        },
        Hole: new[]
        {
            "std_hole_feature",
            "std_hole_sketch",
            "std_hole_cut_feature",
            "std_hole_cut_sketch",
            "std_hole_combine"
        },
        Oval: new[]
        {
            "std_oval_plan",
            "std_oval_feature",
            "std_oval_sketch",
            "std_oval_cut_feature",
            "std_oval_cut_sketch",
            "std_oval_combine"
        },
        Slot: new[]
        {
            "std_slot_plan",
            "std_slot_feature",
            "std_slot_sketch",
            "std_slot_cut_feature",
            "std_slot_cut_sketch",
            "std_slot_combine"
        },
        CBase: new[]
        {
            "std_c_feature",
            "std_c_sketch"
        },
        CFr: "std_fr_c_feature",
        CBr: "std_br_c_feature",
        CCbr: "std_cbr_c_feature",
        Vg: new[]
        {
            "std_vg_feature",
            "std_vg_sketch",
            "std_fr_vg_feature",
            "std_br_vg_feature"
        },
        G: new[]
        {
            "std_g_feature",
            "std_g_sketch",
            "std_g_fr_feature",
            "std_g_br_feature"
        },
        F: new[]
        {
            "std_f_fr_feature",
            "std_f_br_feature"
        },
        RightCut: new[]
        {
            "std_ref_point_right",
            "std_right_cut_plan",
            "std_right_cut"
        },
        LeftCut: new[]
        {
            "std_ref_point_left",
            "std_left_cut_plan",
            "std_left_cut"
        },
        WPgbOverlay: "std_w_pgb_overlay_sketch",
        WFgOverlay: "std_w_fg_overlay_sketch",
        VwCases: new[]
        {
            "std_vw_case1_overlay_sketch",
            "std_vw_case2_overlay_sketch"
        },
        TCases: new[]
        {
            "std_t_case1_overlay_sketch",
            "std_t_case2_overlay_sketch",
            "std_t_case3_overlay_sketch",
            "std_t_case4_overlay_sketch"
        },
        FootOverlays: new[]
        {
            "std_c_overlay_sketch",
            "std_vg_overlay_sketch",
            "std_g_overlay_sketch"
        });

    private static readonly FeatureFamily Rev = new(
        AlwaysOn: new[]
        {
            "td_rev_feature",
            "td_rev_sketch",
            "isa_rev_feature",
            "isa_rev_sketch",
            "ba_rev_feature",
            "ba_rev_sketch",
            "nd_rev_feature",
            "erw_rev_sketch",
            "fro_rev_feature"
        },
        Fro: new[]
        {
            "fro_rev_feature"
        },
        Vr: new[]
        {
            "vr_rev_feature",
            "vr_rev_sketch"
        },
        Slb: new[]
        {
            "slb_rev_feature",
            "slb_rev_sketch"
        },
        W2: new[]
        {
            "w2_rev_feature",
            "w2_rev_sketch"
        },
        Ra2: new[]
        {
            "ra2_rev_feature",
            "ra2_rev_sketch"
        },
        Hole: new[]
        {
            "rev_hole_feature",
            "rev_hole_sketch",
            "rev_hole_cut_feature",
            "rev_hole_cut_sketch",
            "rev_hole_combine"
        },
        Oval: new[]
        {
            "rev_oval_plan",
            "rev_oval_feature",
            "rev_oval_sketch",
            "rev_oval_cut_feature",
            "rev_oval_cut_sketch",
            "rev_oval_combine_feature"
        },
        Slot: new[]
        {
            "rev_slot_plan",
            "rev_slot_feature",
            "rev_slot_sketch",
            "rev_slot_cut_feature",
            "rev_slot_cut_sketch",
            "rev_slot_combine_feature"
        },
        CBase: new[]
        {
            "rev_c_feature",
            "rev_c_sketch"
        },
        CFr: "rev_c_fr_feature",
        CBr: "rev_c_br_feature",
        CCbr: "rev_c_cbr_feature",
        Vg: new[]
        {
            "rev_vg_feature",
            "rev_vg_sketch",
            "rev_vg_fr_feature",
            "rev_vg_br_feature"
        },
        G: new[]
        {
            "rev_g_feature",
            "rev_g_sketch",
            "rev_g_fr_feature",
            "rev_g_br_feature"
        },
        F: new[]
        {
            "rev_f_fr_feature",
            "rev_f_br_feature"
        },
        RightCut: new[]
        {
            "rev_ref_point_right",
            "rev_right_cut_plan",
            "rev_right_cut"
        },
        LeftCut: new[]
        {
            "rev_ref_point_left",
            "rev_left_cut_plan",
            "rev_left_cut"
        },
        WPgbOverlay: "rev_w_pgb_overlay_sketch",
        WFgOverlay: "rev_w_fg_overlay_sketch",
        VwCases: new[]
        {
            "rev_vw_case1_overlay_sketch",
            "rev_vw_case2_overlay_sketch"
        },
        TCases: new[]
        {
            "rev_t_case1_overlay_sketch",
            "rev_t_case2_overlay_sketch",
            "rev_t_case3_overlay_sketch",
            "rev_t_case4_overlay_sketch"
        },
        FootOverlays: new[]
        {
            "rev_c_overlay_sketch",
            "rev_vg_overlay_sketch",
            "rev_g_overlay_sketch"
        });

    private static readonly string[] StdManaged = AllManaged(Std);
    private static readonly string[] RevManaged = AllManaged(Rev);

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

        var active =
            shank == MShankType.Std
                ? Std
                : Rev;

        var hasVr =
            HasAllPositive(
                facts,
                "VR",
                "VRR",
                "VW",
                "VRA");

        /*
         * Overlay W suppression uses the presence of the
         * VR/VW family independently from the full model VR feature.
         */
        var hasOverlayVrFamily =
            HasAnyPositive(
                facts,
                "VR",
                "VRR",
                "VW");

        var hasSlb =
            HasAllPositive(
                facts,
                "VBL",
                "VBLR");

        var hasW2 =
            facts.HasPositive(
                "W2");

        var hasRa2 =
            HasAllPositive(
                facts,
                "RA2",
                "RA2H");

        var hasOverlayVbl =
            facts.HasPositive(
                "VBL");

        var hasOverlayRa2 =
            facts.HasPositive(
                "RA2");

        var vwCase =
            ResolveOverlayVwCase(
                facts,
                hasOverlayVrFamily);

        var feedHole =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFeedHoleType(facts)
                : FeedHoleType.NotApplicable;

        var footOption =
            context.Subclass == WedgeSubclass.FG
                ? ResolveFootOption(facts)
                : FootOptionType.NotApplicable;

        var plan =
            new FeaturePlanBuilder()
                .Know(StdManaged)
                .Know(RevManaged)
                .ForceSuppress(
                    SwNames.EngravingFeature,
                    SwNames.EngravingSketch)
                .ForceSuppress(
                    shank == MShankType.Std
                        ? RevManaged
                        : StdManaged);

        ApplyBaseRules(
            plan,
            active,
            hasVr,
            hasSlb,
            hasW2,
            hasRa2);

        ApplySubclassRules(
            plan,
            facts,
            active,
            context.Subclass,
            feedHole,
            footOption);

        if (context.DrawingType == DrawingType.Overlay)
        {
            ApplyOverlayRules(
                plan,
                context,
                active,
                footOption,
                hasOverlayVbl,
                hasOverlayRa2,
                hasOverlayVrFamily,
                vwCase);
        }
        else
        {
            plan.ForceSuppress(
                OverlayManaged(active));
        }

        Logger.Info(
            "[MFeatureRules] Build -> " +
            $"shank={shank}, " +
            $"subclass={context.Subclass}, " +
            $"drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, " +
            $"feedHole={feedHole}, " +
            $"footOption={footOption}, " +
            $"VR={hasVr}, " +
            $"overlay VR family={hasOverlayVrFamily}, " +
            $"SLB={hasSlb}, " +
            $"W2={hasW2}, " +
            $"RA2={hasRa2}, " +
            $"VW case={vwCase}.");

        return plan.Build();
    }

    private static void ApplyBaseRules(
        FeaturePlanBuilder plan,
        FeatureFamily family,
        bool hasVr,
        bool hasSlb,
        bool hasW2,
        bool hasRa2)
    {
        plan.Activate(
            family.AlwaysOn);

        if (hasVr)
        {
            plan.Activate(
                family.Vr);
        }

        if (hasSlb)
        {
            plan.Activate(
                family.Slb);
        }

        if (hasW2)
        {
            plan.Activate(
                family.W2);
        }

        if (hasRa2)
        {
            plan.Activate(
                family.Ra2);
        }
    }

    private static void ApplySubclassRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FeatureFamily family,
        WedgeSubclass subclass,
        FeedHoleType feedHole,
        FootOptionType footOption)
    {
        var feedManaged =
            FeedHoleManaged(
                family);

        var footManaged =
            FootManaged(
                family);

        plan.Deactivate(
            feedManaged);

        plan.Deactivate(
            footManaged);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                feedManaged);

            plan.ForceSuppress(
                footManaged);

            return;
        }

        switch (feedHole)
        {
            case FeedHoleType.Std:
                plan.Activate(
                    family.Hole);
                break;

            case FeedHoleType.Oval:
                plan.Activate(
                    family.Oval);
                break;

            case FeedHoleType.Slot:
                plan.Activate(
                    family.Slot);
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the M feed-hole type for an FG wedge. " +
                    "Expected STD, Oval or Slot in 'Wed-Feed_H/Slot'.");
        }

        ApplyFootRules(
            plan,
            facts,
            family,
            footOption);
    }

    private static void ApplyFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FeatureFamily family,
        FootOptionType footOption)
    {
        switch (footOption)
        {
            case FootOptionType.C:
                ApplyCFootRules(
                    plan,
                    facts,
                    family);
                break;

            case FootOptionType.Vg:
                plan.Activate(
                    family.Vg);
                break;

            case FootOptionType.G:
                plan.Activate(
                    family.G);
                break;

            case FootOptionType.F:
                plan.Activate(
                    family.F);

                plan.ForceSuppress(
                    family.Fro);
                break;

            case FootOptionType.Cc:
                break;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the M foot option for an FG wedge. " +
                    "Expected LW_C, LW_VG, LW_G, LW_F or LW_CC.");
        }
    }

    private static void ApplyCFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FeatureFamily family)
    {
        var froEqualsFr =
            ResolveFroEqualsFr(
                facts);

        var hasCbrl =
            facts.HasPositive(
                "CBRL");

        var hasCbrd =
            facts.HasPositive(
                "CBRD");

        if (hasCbrl != hasCbrd)
        {
            throw new InvalidOperationException(
                "M C foot has incomplete CBR dimensions. " +
                "CBRL and CBRD must either both be > 0 or both be absent/zero.");
        }

        var hasCbr =
            hasCbrl &&
            hasCbrd;

        plan.Activate(
            family.CBase);

        if (hasCbr)
        {
            plan.ForceSuppress(
                family.CBr);

            plan.Activate(
                family.CCbr);

            if (!froEqualsFr)
            {
                plan.Activate(
                    family.CFr);
            }

            return;
        }

        plan.ForceSuppress(
            family.CCbr);

        plan.Activate(
            family.CBr);

        if (!froEqualsFr)
        {
            plan.Activate(
                family.CFr);
        }
    }

    // ================================================================
    // OVERLAY RULES
    // ================================================================

    private static void ApplyOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FeatureFamily family,
        FootOptionType footOption,
        bool hasVbl,
        bool hasRa2,
        bool hasOverlayVrFamily,
        OverlayVwCase vwCase)
    {
        plan.Deactivate(
            OverlayManaged(family));

        ApplyOverlayCutRule(
            plan,
            context,
            family);

        ApplyOverlayWRule(
            plan,
            context.Subclass,
            family,
            hasOverlayVrFamily);

        ActivateOverlayVwCase(
            plan,
            family,
            vwCase);

        var tSketch =
            hasVbl
                ? hasRa2
                    ? family.TCases[3]
                    : family.TCases[1]
                : hasRa2
                    ? family.TCases[2]
                    : family.TCases[0];

        plan.ActivateOnly(
            tSketch,
            family.TCases);

        if (context.Subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                family.FootOverlays);

            return;
        }

        var footSketch =
            footOption switch
            {
                FootOptionType.C =>
                    family.FootOverlays[0],

                FootOptionType.Vg =>
                    family.FootOverlays[1],

                FootOptionType.G =>
                    family.FootOverlays[2],

                FootOptionType.F =>
                    null,

                _ =>
                    null
            };

        if (footSketch is null)
        {
            plan.ForceSuppress(
                family.FootOverlays);

            return;
        }

        plan.ActivateOnly(
            footSketch,
            family.FootOverlays);
    }

    private static void ApplyOverlayWRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        FeatureFamily family,
        bool hasOverlayVrFamily)
    {
        var wOverlaySketches =
            new[]
            {
                family.WPgbOverlay,
                family.WFgOverlay
            };

        plan.Deactivate(
            wOverlaySketches);

        /*
         * VR/VW family replaces the standalone W overlay sketch.
         *
         * Therefore both possible W overlay sketches are
         * explicitly suppressed whenever VR/VW is present.
         */
        if (hasOverlayVrFamily)
        {
            plan.ForceSuppress(
                wOverlaySketches);

            Logger.Info(
                "[MFeatureRules] Overlay W -> " +
                "VR/VW family present; W overlay sketches suppressed.");

            return;
        }

        plan.ActivateOnly(
            subclass == WedgeSubclass.PGB
                ? family.WPgbOverlay
                : family.WFgOverlay,
            wOverlaySketches);
    }

    private static void ActivateOverlayVwCase(
        FeaturePlanBuilder plan,
        FeatureFamily family,
        OverlayVwCase vwCase)
    {
        plan.Deactivate(
            family.VwCases);

        if (vwCase == OverlayVwCase.None)
        {
            plan.ForceSuppress(
                family.VwCases);

            return;
        }

        plan.ActivateOnly(
            vwCase == OverlayVwCase.Case1
                ? family.VwCases[0]
                : family.VwCases[1],
            family.VwCases);
    }

    private static void ApplyOverlayCutRule(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FeatureFamily family)
    {
        var view =
            NormalizePackedToken(
                context.TargetConfigurationName);

        switch (view)
        {
            case "LEFT_VIEW":
                plan.Activate(
                    family.LeftCut);

                plan.ForceSuppress(
                    family.RightCut);
                break;

            case "RIGHT_VIEW":
                plan.Activate(
                    family.RightCut);

                plan.ForceSuppress(
                    family.LeftCut);
                break;

            default:
                plan.ForceSuppress(
                    family.LeftCut);

                plan.ForceSuppress(
                    family.RightCut);
                break;
        }
    }

    private static MShankType ResolveShankType(
        WedgeFacts facts)
    {
        var token =
            NormalizePackedToken(
                facts.NormalizedPropertyToken(
                    "Wed-Type",
                    "Wed_Type",
                    "Wed Type",
                    "Wedge-Type",
                    "Wedge_Type",
                    "wedge_type"));

        return token switch
        {
            "SW_STD" or
            "STD" =>
                MShankType.Std,

            "SW_180REV" or
            "SW_180_REV" or
            "180REV" or
            "180_REV" =>
                MShankType.Rev,

            _ =>
                throw new InvalidOperationException(
                    "Unable to resolve the M shank from 'Wed-Type'. " +
                    "Expected SW_STD or SW_180REV, but received " +
                    $"'{DisplayToken(token)}'.")
        };
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

            "LW_CC" or
            "SW_CC" or
            "CC" =>
                FootOptionType.Cc,

            _ =>
                FootOptionType.Unknown
        };
    }

    private static OverlayVwCase ResolveOverlayVwCase(
        WedgeFacts facts,
        bool hasOverlayVrFamily)
    {
        if (!hasOverlayVrFamily)
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
                "[MFeatureRules] VR/VW is present but W is missing/not a length. " +
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
                "Cannot apply M C-foot rules because FRO is missing/not a length.");
        }

        if (!facts.TryGetLengthMm(
                "FR",
                out var fr))
        {
            throw new InvalidOperationException(
                "Cannot apply M C-foot rules because FR is missing/not a length.");
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

    private static bool HasAnyPositive(
        WedgeFacts facts,
        params string[] keys)
        => keys.Any(
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

    private static string DisplayToken(
        string token)
        => string.IsNullOrWhiteSpace(token)
            ? "<missing>"
            : token;

    private static string[] FeedHoleManaged(
        FeatureFamily family)
        => family.Hole
            .Concat(family.Oval)
            .Concat(family.Slot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] FootManaged(
        FeatureFamily family)
        => family.CBase
            .Concat(
                new[]
                {
                    family.CFr,
                    family.CBr,
                    family.CCbr
                })
            .Concat(family.Vg)
            .Concat(family.G)
            .Concat(family.F)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] OverlayManaged(
        FeatureFamily family)
        => family.RightCut
            .Concat(family.LeftCut)
            .Concat(
                new[]
                {
                    family.WPgbOverlay,
                    family.WFgOverlay
                })
            .Concat(family.VwCases)
            .Concat(family.TCases)
            .Concat(family.FootOverlays)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] AllManaged(
        FeatureFamily family)
        => family.AlwaysOn
            .Concat(family.Fro)
            .Concat(family.Vr)
            .Concat(family.Slb)
            .Concat(family.W2)
            .Concat(family.Ra2)
            .Concat(FeedHoleManaged(family))
            .Concat(FootManaged(family))
            .Concat(OverlayManaged(family))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record FeatureFamily(
        string[] AlwaysOn,
        string[] Fro,
        string[] Vr,
        string[] Slb,
        string[] W2,
        string[] Ra2,
        string[] Hole,
        string[] Oval,
        string[] Slot,
        string[] CBase,
        string CFr,
        string CBr,
        string CCbr,
        string[] Vg,
        string[] G,
        string[] F,
        string[] RightCut,
        string[] LeftCut,
        string WPgbOverlay,
        string WFgOverlay,
        string[] VwCases,
        string[] TCases,
        string[] FootOverlays);

    private enum MShankType
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
        Vg,
        G,
        F,
        Cc
    }

    private enum OverlayVwCase
    {
        None,
        Case1,
        Case2
    }
}
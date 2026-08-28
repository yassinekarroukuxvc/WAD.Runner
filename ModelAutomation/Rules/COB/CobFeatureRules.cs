using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.COB;

/// <summary>
/// COB-only feature rules. COB no longer inherits any COB-like feature behavior.
/// </summary>
public sealed class CobFeatureRules : IFeatureRuleSet
{
    private static readonly FeatureFamily Std = new(
        AlwaysOn: new[]
        {
            "td_std_feature", "td_std_sketch",
            "isa_std_feature", "isa_std_sketch",
            "ba_std_feature", "ba_std_sketch",
            "fro_std_feature", "fro_std_sketch",
            "erw_std_feature", "erw_std_sketch",
            "core_fillet_std_feature"
        },
        Vr: new[] { "vr_std_feature", "vr_std_sketch" },
        Slb: new[] { "slb_std_feature", "slb_std_sketch" },
        W2: new[] { "w2_std_feature", "w2_std_sketch" },
        Ra2: new[] { "ra2_std_feature", "ra2_std_sketch" },
        Hole: new[]
        {
            "std_hole_feature", "std_hole_sketch",
            "std_hole_cut_feature", "std_hole_cut_sketch",
            "std_hole_combine"
        },
        Oval: new[]
        {
            "std_oval_plan",
            "std_oval_feature", "std_oval_sketch",
            "std_oval_cut_feature", "std_oval_cut_sketch",
            "std_oval_combine"
        },
        Slot: new[]
        {
            "std_slot_plan",
            "std_slot_feature", "std_slot_sketch",
            "std_slot_cut_feature", "std_slot_cut_sketch",
            "std_slot_combine"
        },
        CBase: new[] { "std_c_feature", "std_c_sketch" },
        CFr: "std_fr_c_feature",
        CBr: "std_br_c_feature",
        CCbr: "std_cbr_c_feature",
        CCbrCore: "std_cbr_c_core_feature",
        CRoundBr: "std_c_round_br_feature",
        Vg: new[]
        {
            "std_vg_feature", "std_vg_sketch",
            "std_fr_vg_feature", "std_br_vg_feature",
            "std_vg_round_br_feature"
        },
        G: new[]
        {
            "std_g_feature", "std_g_sketch",
            "std_g_fr_feature", "std_g_br_feature",
            "std_g_round_br_feature"
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
            "td_rev_feature", "td_rev_sketch",
            "isa_rev_feature", "isa_rev_sketch",
            "ba_rev_feature", "ba_rev_sketch",
            "fro_rev_feature",
            "erw_rev_feature", "erw_rev_sketch",
            "core_fillet_rev_feature",
            "rev_round_br"
        },
        Vr: new[] { "vr_rev_feature", "vr_rev_sketch" },
        Slb: new[] { "slb_rev_feature", "slb_rev_sketch" },
        W2: new[] { "w2_rev_feature", "w2_rev_sketch" },
        Ra2: new[] { "ra2_rev_feature", "ra2_rev_sketch" },
        Hole: new[]
        {
            "rev_hole_feature", "rev_hole_sketch",
            "rev_hole_cut_feature", "rev_hole_cut_sketch",
            "rev_hole_combine"
        },
        Oval: new[]
        {
            "rev_oval_plan",
            "rev_oval_feature", "rev_oval_sketch",
            "rev_oval_cut_feature", "rev_oval_cut_sketch",
            "rev_oval_combine_feature"
        },
        Slot: new[]
        {
            "rev_slot_plan",
            "rev_slot_feature", "rev_slot_sketch",
            "rev_slot_cut_feature", "rev_slot_cut_sketch",
            "rev_slot_combine_feature"
        },
        CBase: new[] { "rev_c_feature", "rev_c_sketch" },
        CFr: "rev_c_fr_feature",
        CBr: "rev_c_br_feature",
        CCbr: "rev_c_cbr_feature",
        CCbrCore: "rev_cbr_c_core_feature",
        CRoundBr: "rev_c_round_br_feature",
        Vg: new[]
        {
            "rev_vg_feature", "rev_vg_sketch",
            "rev_vg_fr_feature", "rev_vg_br_feature",
            "rev_vg_round_br_feature"
        },
        G: new[]
        {
            "rev_g_feature", "rev_g_sketch",
            "rev_g_fr_feature", "rev_g_br_feature",
            "rev_g_round_br_feature"
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

    public ModelRuleRunner.FeaturePlan Build(WedgeData wedge, FeatureRuleContext context)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (context is null) throw new ArgumentNullException(nameof(context));

        var facts = new WedgeFacts(wedge);
        var shank = ResolveShankType(facts);
        var family = shank == CobShankType.Std ? Std : Rev;

        var hasVr = HasAllPositive(facts, "VR", "VW", "VRR", "VRA");
        var hasSlb = HasAllPositive(facts, "VBL", "VBLR", "T");
        var hasW2 = facts.HasPositive("W2");
        var hasRa2 = HasAllPositive(facts, "RA2", "RA2H");
        var hasOverlayVbl = facts.HasPositive("VBL");
        var hasOverlayRa2 = facts.HasPositive("RA2");
        var vwCase = ResolveOverlayVwCase(facts);

        var feedHole = context.Subclass == WedgeSubclass.FG
            ? ResolveFeedHoleType(facts)
            : FeedHoleType.NotApplicable;

        var footOption = context.Subclass == WedgeSubclass.FG
            ? ResolveFootOption(facts)
            : FootOptionType.NotApplicable;

        var plan = new FeaturePlanBuilder()
            .Know(StdManaged)
            .Know(RevManaged)
            .ForceSuppress(SwNames.EngravingFeature, SwNames.EngravingSketch)
            .ForceSuppress(shank == CobShankType.Std ? RevManaged : StdManaged);

        ApplyBaseRules(plan, family, hasVr, hasSlb, hasW2, hasRa2);
        ApplySubclassRules(plan, facts, family, context.Subclass, feedHole, footOption);

        if (context.DrawingType == DrawingType.Overlay)
            ApplyOverlayRules(
                plan,
                context,
                family,
                footOption,
                hasOverlayVbl,
                hasOverlayRa2,
                vwCase);
        else
            plan.ForceSuppress(OverlayManaged(family));

        Logger.Info(
            "[CobFeatureRules] Build -> " +
            $"shank={shank}, subclass={context.Subclass}, drawingType={context.DrawingType}, " +
            $"targetConfig={context.TargetConfigurationName}, " +
            $"feedHole={feedHole}, footOption={footOption}, VR={hasVr}, SLB={hasSlb}, " +
            $"W2={hasW2}, RA2={hasRa2}, VW case={vwCase}.");

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
        plan.Activate(family.AlwaysOn);
        if (hasVr) plan.Activate(family.Vr);
        if (hasSlb) plan.Activate(family.Slb);
        if (hasW2) plan.Activate(family.W2);
        if (hasRa2) plan.Activate(family.Ra2);
    }

    private static void ApplySubclassRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FeatureFamily family,
        WedgeSubclass subclass,
        FeedHoleType feedHole,
        FootOptionType footOption)
    {
        var feedManaged = FeedHoleManaged(family);
        var footManaged = FootManaged(family);

        plan.Deactivate(feedManaged);
        plan.Deactivate(footManaged);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(feedManaged);
            plan.ForceSuppress(footManaged);
            return;
        }

        switch (feedHole)
        {
            case FeedHoleType.Std:
                plan.Activate(family.Hole);
                break;
            case FeedHoleType.Oval:
                plan.Activate(family.Oval);
                break;
            case FeedHoleType.Slot:
                plan.Activate(family.Slot);
                break;
            default:
                throw new InvalidOperationException(
                    "Unable to resolve the COB feed-hole type for an FG wedge. " +
                    "Expected STD, Oval or Slot in 'Wed-Feed_H/Slot'.");
        }

        ApplyFootRules(plan, facts, family, footOption);
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
                ApplyCFootRules(plan, facts, family);
                break;
            case FootOptionType.Vg:
                plan.Activate(family.Vg);
                break;
            case FootOptionType.G:
                plan.Activate(family.G);
                break;
            case FootOptionType.Other:
            case FootOptionType.NotApplicable:
                break;
            default:
                break;
        }
    }

    private static void ApplyCFootRules(
        FeaturePlanBuilder plan,
        WedgeFacts facts,
        FeatureFamily family)
    {
        var hasCbrl = facts.HasPositive("CBRL");
        var hasCbrd = facts.HasPositive("CBRD");

        if (hasCbrl != hasCbrd)
        {
            throw new InvalidOperationException(
                "COB C foot has incomplete CBR dimensions. " +
                "CBRL and CBRD must either both be > 0 or both be absent/zero.");
        }

        var froEqualsFr = ResolveFroEqualsFr(facts);
        var hasCbr = hasCbrl && hasCbrd;

        plan.Activate(family.CBase);
        plan.Activate(family.CRoundBr);

        if (hasCbr)
        {
            plan.Activate(family.CCbr, family.CCbrCore);
            if (!froEqualsFr) plan.Activate(family.CFr);
            return;
        }

        plan.Activate(family.CBr);
        if (!froEqualsFr) plan.Activate(family.CFr);
    }

    private static void ApplyOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FeatureFamily family,
        FootOptionType footOption,
        bool hasVbl,
        bool hasRa2,
        OverlayVwCase vwCase)
    {
        plan.Deactivate(OverlayManaged(family));

        ApplyOverlayCutRule(
            plan,
            context,
            family);

        plan.ActivateOnly(
            context.Subclass == WedgeSubclass.PGB ? family.WPgbOverlay : family.WFgOverlay,
            new[] { family.WPgbOverlay, family.WFgOverlay });

        if (vwCase != OverlayVwCase.None)
        {
            plan.ActivateOnly(
                vwCase == OverlayVwCase.Case1 ? family.VwCases[0] : family.VwCases[1],
                family.VwCases);
        }

        var tSketch = hasVbl
            ? hasRa2 ? family.TCases[3] : family.TCases[1]
            : hasRa2 ? family.TCases[2] : family.TCases[0];

        plan.ActivateOnly(tSketch, family.TCases);

        if (context.Subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(family.FootOverlays);
            return;
        }

        var footSketch = footOption switch
        {
            FootOptionType.C => family.FootOverlays[0],
            FootOptionType.Vg => family.FootOverlays[1],
            FootOptionType.G => family.FootOverlays[2],
            _ => null
        };

        if (footSketch is not null)
            plan.ActivateOnly(footSketch, family.FootOverlays);
        else
            plan.ForceSuppress(family.FootOverlays);
    }

    private static void ApplyOverlayCutRule(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        FeatureFamily family)
    {
        var configuration =
            NormalizePackedToken(
                context.TargetConfigurationName);

        switch (configuration)
        {
            case "RIGHT_VIEW":
                plan.Activate(family.RightCut);
                plan.ForceSuppress(family.LeftCut);
                break;

            case "LEFT_VIEW":
                plan.Activate(family.LeftCut);
                plan.ForceSuppress(family.RightCut);
                break;

            default:
                plan.ForceSuppress(family.RightCut);
                plan.ForceSuppress(family.LeftCut);

                Logger.Warn(
                    "[CobFeatureRules] Overlay configuration is neither " +
                    "'right_view' nor 'left_view'. Both cut/reference " +
                    $"families were suppressed. Received '{context.TargetConfigurationName}'.");
                break;
        }
    }

    private static CobShankType ResolveShankType(WedgeFacts facts)
    {
        var token = NormalizePackedToken(facts.NormalizedPropertyToken(
            "Wed-Type", "Wed_Type", "Wed Type", "Wedge-Type", "Wedge_Type", "wedge_type"));

        return token switch
        {
            "SW_STD" or "STD" => CobShankType.Std,
            "SW_180REV" or "SW_180_REV" or "180REV" or "180_REV" => CobShankType.Rev,
            _ => throw new InvalidOperationException(
                "Unable to resolve the COB shank from 'Wed-Type'. " +
                $"Expected SW_STD or SW_180REV, but received '{DisplayToken(token)}'.")
        };
    }

    private static FeedHoleType ResolveFeedHoleType(WedgeFacts facts)
    {
        var raw = facts.NormalizedPropertyToken(
            "Wed-Feed_H/Slot", "Wed_Feed_H_Slot", "Wed Feed H Slot", "Wed-Feed H Slot",
            "Feed_H/Slot", "Feed_H_Slot", "Feed H Slot", "feed_h_slot");

        var token = NormalizeFeedHoleToken(raw);
        return token switch
        {
            "STD" => FeedHoleType.Std,
            "OVAL" => FeedHoleType.Oval,
            "SLOT" => FeedHoleType.Slot,
            _ => FeedHoleType.Unknown
        };
    }

    private static FootOptionType ResolveFootOption(WedgeFacts facts)
    {
        var token = NormalizePackedToken(facts.NormalizedPropertyToken(
            "Wed-Foot_Option", "Wed_Foot_Option", "Wed Foot Option", "Wed-Foot Option",
            "Foot_Option", "Foot Option", "foot_option"));

        return token switch
        {
            "LW_C" or "SW_C" or "C" => FootOptionType.C,
            "LW_VG" or "SW_VG" or "VG" => FootOptionType.Vg,
            "LW_G" or "SW_G" or "G" => FootOptionType.G,
            _ => FootOptionType.Other
        };
    }

    private static OverlayVwCase ResolveOverlayVwCase(WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm("VW", out var vw) || vw <= WedgeFacts.DefaultPositiveEpsilon)
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm("W", out var w))
        {
            Logger.Warn("[CobFeatureRules] VW is present but W is missing/not a length. No VW overlay case selected.");
            return OverlayVwCase.None;
        }

        return decimal.Abs(vw - w) <= WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVwCase.Case1
            : OverlayVwCase.Case2;
    }

    private static bool ResolveFroEqualsFr(WedgeFacts facts)
    {
        if (!facts.TryGetLengthMm("FRO", out var fro))
            throw new InvalidOperationException("Cannot apply COB C-foot rules because FRO is missing/not a length.");

        if (!facts.TryGetLengthMm("FR", out var fr))
            throw new InvalidOperationException("Cannot apply COB C-foot rules because FR is missing/not a length.");

        return decimal.Abs(fro - fr) <= WedgeFacts.DefaultPositiveEpsilon;
    }

    private static bool HasAllPositive(WedgeFacts facts, params string[] keys)
        => keys.All(key => facts.HasPositive(key));

    private static string NormalizeFeedHoleToken(string? raw)
    {
        var token = RemovePackedDatabaseSuffix(raw).Trim().ToUpperInvariant();
        if (token.StartsWith("STD", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("STANDARD", StringComparison.OrdinalIgnoreCase)) return "STD";
        if (token.StartsWith("OVAL", StringComparison.OrdinalIgnoreCase)) return "OVAL";
        if (token.StartsWith("SLOT", StringComparison.OrdinalIgnoreCase)) return "SLOT";
        return token;
    }

    private static string NormalizePackedToken(string? raw)
    {
        var token = RemovePackedDatabaseSuffix(raw)
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .Trim('_')
            .ToUpperInvariant();

        while (token.Contains("__", StringComparison.Ordinal))
            token = token.Replace("__", "_", StringComparison.Ordinal);

        return token;
    }

    private static string RemovePackedDatabaseSuffix(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var token = raw.Trim().Trim('\0');
        var separatorIndex = token.IndexOf(';');
        return separatorIndex >= 0 ? token[..separatorIndex] : token;
    }

    private static string DisplayToken(string token)
        => string.IsNullOrWhiteSpace(token) ? "<missing>" : token;

    private static string[] FeedHoleManaged(FeatureFamily family)
        => family.Hole.Concat(family.Oval).Concat(family.Slot)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] FootManaged(FeatureFamily family)
        => family.CBase
            .Concat(new[] { family.CFr, family.CBr, family.CCbr, family.CCbrCore, family.CRoundBr })
            .Concat(family.Vg)
            .Concat(family.G)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] OverlayManaged(FeatureFamily family)
        => family.RightCut.Concat(family.LeftCut)
            .Concat(new[] { family.WPgbOverlay, family.WFgOverlay })
            .Concat(family.VwCases)
            .Concat(family.TCases)
            .Concat(family.FootOverlays)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string[] AllManaged(FeatureFamily family)
        => family.AlwaysOn
            .Concat(family.Vr)
            .Concat(family.Slb)
            .Concat(family.W2)
            .Concat(family.Ra2)
            .Concat(FeedHoleManaged(family))
            .Concat(FootManaged(family))
            .Concat(OverlayManaged(family))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private sealed record FeatureFamily(
        string[] AlwaysOn,
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
        string CCbrCore,
        string CRoundBr,
        string[] Vg,
        string[] G,
        string[] RightCut,
        string[] LeftCut,
        string WPgbOverlay,
        string WFgOverlay,
        string[] VwCases,
        string[] TCases,
        string[] FootOverlays);

    private enum CobShankType { Std, Rev }
    private enum FeedHoleType { NotApplicable, Unknown, Std, Oval, Slot }
    private enum FootOptionType { NotApplicable, C, Vg, G, Other }
    private enum OverlayVwCase { None, Case1, Case2 }
}
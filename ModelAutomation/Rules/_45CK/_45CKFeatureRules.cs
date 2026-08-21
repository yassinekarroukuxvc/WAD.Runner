using Microsoft.AspNetCore.Identity;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;
using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Rules._45CK;

/// <summary>
/// 45CK model feature rules.
///
/// PGB:
///     td/isa/ba always on.
///     vr on when VR, VRR, VW and VRA are all > 0.
///     w2 on when W2 > 0.
///     slb on when VBL and VBLR are both > 0.
///     ra2 on when RA2 and RA2H are both > 0.
///     all feed-hole and foot-option features suppressed.
///
/// FG:
///     same PGB base rules plus ERW, core fillet, FRO and C always on.
///     STD/Oval/Slot selects exactly one feed-hole family.
///     LW_VG selects VG + FR + BR; LW_CG selects CG.
///
/// Overlay:
///     right_view activates only the right cut/reference family.
///     left_view activates only the left cut/reference family.
///     right_view selects the PGB/FG W sketch and the VR case sketch.
///     left_view selects the T case sketch; VG overlay is available in both overlay configs.
///     T Case 1 = no VBL/no RA2; Case 2 = VBL; Case 3 = RA2; Case 4 = both.
/// </summary>
public sealed class _45CKFeatureRules : IFeatureRuleSet
{
    private static readonly string[] BaseAlwaysOnNames =
    {
        "td_feature", "td_sketch",
        "isa_feature", "isa_sketch",
        "ba_feature", "ba_sketch"
    };

    private static readonly string[] VrNames =
    {
        "vr_feature", "vr_sketch"
    };

    private static readonly string[] W2Names =
    {
        "w2_feature", "w2_sketch"
    };

    private static readonly string[] SlbNames =
    {
        "slb_feature", "slb_sketch"
    };

    private static readonly string[] Ra2Names =
    {
        "ra2_feature", "ra2_sketch"
    };

    private static readonly string[] FgAlwaysOnNames =
    {
        "erw_feature", "erw_sketch",
        "core_fillet_feature",
        "fro_feature", "fro_sketch",
        "c_feature", "c_sketch"
    };

    private static readonly string[] StdHoleNames =
    {
        "hole_feature", "hole_sketch",
        "hole_cut_feature", "hole_cut_sketch",
        "hole_combine_feature"
    };

    private static readonly string[] OvalHoleNames =
    {
        "oval_plan",
        "oval_feature", "oval_sketch",
        "oval_cut_feature", "oval_cut_sketch",
        "oval_combine_feature"
    };

    private static readonly string[] SlotHoleNames =
    {
        "slot_plan",
        "slot_feature", "slot_sketch",
        "slot_cut_feature", "slot_cut_sketch",
        "slot_combine_feature"
    };

    private static readonly string[] FeedHoleManagedNames =
    {
        "hole_feature", "hole_sketch", "hole_cut_feature", "hole_cut_sketch", "hole_combine_feature",
        "oval_plan", "oval_feature", "oval_sketch", "oval_cut_feature", "oval_cut_sketch", "oval_combine_feature",
        "slot_plan", "slot_feature", "slot_sketch", "slot_cut_feature", "slot_cut_sketch", "slot_combine_feature"
    };

    private static readonly string[] VgFootNames =
    {
        "vg_feature", "vg_sketch", "fr_feature", "br_feature"
    };

    private static readonly string[] CgFootNames =
    {
        "cg_feature"
    };

    private static readonly string[] FootManagedNames =
    {
        "vg_feature", "vg_sketch", "fr_feature", "br_feature", "cg_feature"
    };

    private static readonly string[] RightOverlayCutNames =
    {
        "ref_point_right", "right_cut_plan", "right_cut_feature"
    };

    private static readonly string[] LeftOverlayCutNames =
    {
        "ref_point_left", "left_cut_plan", "left_cut_feature"
    };

    private const string WPgbOverlaySketch = "w_pgb_overlay_sketch";
    private const string WFgOverlaySketch = "w_fg_overlay_sketch";

    private static readonly string[] VrCaseOverlaySketches =
    {
        "vr_case1_overlay_sketch", "vr_case2_overlay_sketch"
    };

    private const string VgOverlaySketch = "vg_overlay_sketch";

    private static readonly string[] TCaseOverlaySketches =
    {
        "t_case1_overlay_sketch", "t_case2_overlay_sketch",
        "t_case3_overlay_sketch", "t_case4_overlay_sketch"
    };

    private static readonly string[] OverlayManagedNames =
    {
        "ref_point_right", "right_cut_plan", "right_cut_feature",
        "ref_point_left", "left_cut_plan", "left_cut_feature",
        "w_pgb_overlay_sketch", "w_fg_overlay_sketch",
        "vr_case1_overlay_sketch", "vr_case2_overlay_sketch",
        "vg_overlay_sketch",
        "t_case1_overlay_sketch", "t_case2_overlay_sketch",
        "t_case3_overlay_sketch", "t_case4_overlay_sketch"
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
        var isFg = context.Subclass == WedgeSubclass.FG;
        var isPgb = context.Subclass == WedgeSubclass.PGB;

        if (!isFg && !isPgb)
        {
            throw new InvalidOperationException(
                $"45CK supports only FG and PGB subclasses, but received '{context.Subclass}'.");
        }

        var hasVr = HasAllPositiveNominal(facts, "VR", "VRR", "VW", "VRA");
        var hasW2 = facts.HasPositive("W2");
        var hasSlb = HasAllPositiveNominal(facts, "VBL", "VBLR");
        var hasRa2 = HasAllPositiveNominal(facts, "RA2", "RA2H");
        var overlayVwCase = ResolveOverlayVwCase(facts);
        var feedHoleType = isFg ? ResolveFeedHoleType(facts) : FeedHoleType.NotApplicable;
        var footOption = isFg ? ResolveFootOption(facts) : FootOptionType.NotApplicable;

        var plan = new FeaturePlanBuilder()
            .Know(BaseAlwaysOnNames)
            .Know(VrNames)
            .Know(W2Names)
            .Know(SlbNames)
            .Know(Ra2Names)
            .Know(FgAlwaysOnNames)
            .Know(FeedHoleManagedNames)
            .Know(FootManagedNames)
            .Know(OverlayManagedNames)
            .ForceSuppress(SwNames.EngravingFeature, SwNames.EngravingSketch);

        plan.Activate(BaseAlwaysOnNames);

        if (hasVr)
            plan.Activate(VrNames);

        if (hasW2)
            plan.Activate(W2Names);

        if (hasSlb)
            plan.Activate(SlbNames);

        if (hasRa2)
            plan.Activate(Ra2Names);

        if (isPgb)
        {
            plan.ForceSuppress(FgAlwaysOnNames);
            plan.ForceSuppress(FeedHoleManagedNames);
            plan.ForceSuppress(FootManagedNames);
        }
        else
        {
            plan.Activate(FgAlwaysOnNames);
            ApplyFeedHoleRules(plan, feedHoleType);
            ApplyFootRules(plan, footOption);
        }

        if (context.DrawingType == DrawingType.Overlay)
        {
            var overlayView = ResolveOverlayViewConfiguration(context.TargetConfigurationName);

            ApplyOverlayCutViewRule(plan, overlayView);
            ApplyOverlayRightViewRules(plan, context.Subclass, overlayView, overlayVwCase);
            ApplyOverlayLeftViewRules(plan, overlayView, facts.HasPositive("VBL"), facts.HasPositive("RA2"));
            ApplyOverlayFootRule(plan, context.Subclass, footOption);
        }
        else
        {
            plan.ForceSuppress(OverlayManagedNames);
        }

        Logger.Info(
            "[_45CKFeatureRules] Plan -> " +
            $"subclass={context.Subclass}, drawingType={context.DrawingType}, " +
            $"config={context.TargetConfigurationName}, feedHole={feedHoleType}, " +
            $"footOption={footOption}, VR={hasVr}, W2={hasW2}, SLB={hasSlb}, " +
            $"RA2={hasRa2}, overlayVRCase={overlayVwCase}.");

        return plan.Build();
    }

    private static void ApplyFeedHoleRules(
        FeaturePlanBuilder plan,
        FeedHoleType feedHoleType)
    {
        plan.Deactivate(FeedHoleManagedNames);

        switch (feedHoleType)
        {
            case FeedHoleType.Std:
                plan.Activate(StdHoleNames);
                return;

            case FeedHoleType.Oval:
                plan.Activate(OvalHoleNames);
                return;

            case FeedHoleType.Slot:
                plan.Activate(SlotHoleNames);
                return;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the 45CK feed-hole type for an FG wedge. " +
                    "Expected STD, Oval or Slot in 'Wed-Feed_H/Slot'.");
        }
    }

    private static FeedHoleType ResolveFeedHoleType(WedgeFacts facts)
    {
        var raw = facts.NormalizedPropertyToken(
            "Wed-Feed_H/Slot",
            "Wed_Feed_H_Slot",
            "Wed Feed H Slot",
            "Wed-Feed H Slot",
            "Feed_H/Slot",
            "Feed_H_Slot",
            "Feed H Slot",
            "feed_h_slot");

        var token = NormalizeFeedHoleToken(raw);

        return token switch
        {
            "STD" => FeedHoleType.Std,
            "OVAL" => FeedHoleType.Oval,
            "SLOT" => FeedHoleType.Slot,
            _ => FeedHoleType.Unknown
        };
    }

    private static string NormalizeFeedHoleToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token = RemovePackedDatabaseSuffix(raw).Trim().ToUpperInvariant();

        if (token.StartsWith("STD", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("STANDARD", StringComparison.OrdinalIgnoreCase))
        {
            return "STD";
        }

        if (token.StartsWith("OVAL", StringComparison.OrdinalIgnoreCase))
            return "OVAL";

        if (token.StartsWith("SLOT", StringComparison.OrdinalIgnoreCase))
            return "SLOT";

        return token;
    }

    private static void ApplyFootRules(
        FeaturePlanBuilder plan,
        FootOptionType footOption)
    {
        plan.Deactivate(FootManagedNames);

        switch (footOption)
        {
            case FootOptionType.Vg:
                plan.Activate(VgFootNames);
                return;

            case FootOptionType.Cg:
                plan.Activate(CgFootNames);
                return;

            default:
                throw new InvalidOperationException(
                    "Unable to resolve the 45CK foot option for an FG wedge. " +
                    "Expected LW_VG or LW_CG in 'Wed-Foot_Option'.");
        }
    }

    private static FootOptionType ResolveFootOption(WedgeFacts facts)
    {
        var raw = facts.NormalizedPropertyToken(
            "Wed-Foot_Option",
            "Wed_Foot_Option",
            "Wed Foot Option",
            "Wed-Foot Option",
            "Foot_Option",
            "Foot Option",
            "foot_option");

        var token = NormalizePackedToken(raw);

        return token switch
        {
            "LW_VG" or "VG" => FootOptionType.Vg,
            "LW_CG" or "CG" => FootOptionType.Cg,
            _ => FootOptionType.Unknown
        };
    }

    private static void ApplyOverlayCutViewRule(
        FeaturePlanBuilder plan,
        OverlayViewConfiguration overlayView)
    {
        switch (overlayView)
        {
            case OverlayViewConfiguration.Left:
                plan.Activate(LeftOverlayCutNames);
                plan.ForceSuppress(RightOverlayCutNames);
                break;

            case OverlayViewConfiguration.Right:
                plan.Activate(RightOverlayCutNames);
                plan.ForceSuppress(LeftOverlayCutNames);
                break;

            default:
                plan.ForceSuppress(LeftOverlayCutNames);
                plan.ForceSuppress(RightOverlayCutNames);
                break;
        }
    }

    private static OverlayViewConfiguration ResolveOverlayViewConfiguration(string? configurationName)
    {
        return NormalizePackedToken(configurationName) switch
        {
            "LEFT_VIEW" => OverlayViewConfiguration.Left,
            "RIGHT_VIEW" => OverlayViewConfiguration.Right,
            _ => OverlayViewConfiguration.None
        };
    }

    private static void ApplyOverlayRightViewRules(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        OverlayViewConfiguration overlayView,
        OverlayVwCase overlayVwCase)
    {
        plan.Deactivate(WPgbOverlaySketch, WFgOverlaySketch);
        plan.Deactivate(VrCaseOverlaySketches);

        if (overlayView != OverlayViewConfiguration.Right)
        {
            plan.ForceSuppress(WPgbOverlaySketch, WFgOverlaySketch);
            plan.ForceSuppress(VrCaseOverlaySketches);
            return;
        }

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ActivateOnly(
                WPgbOverlaySketch,
                new[] { WPgbOverlaySketch, WFgOverlaySketch });
        }
        else
        {
            plan.ActivateOnly(
                WFgOverlaySketch,
                new[] { WPgbOverlaySketch, WFgOverlaySketch });
        }

        if (overlayVwCase == OverlayVwCase.None)
            return;

        plan.ActivateOnly(
            overlayVwCase == OverlayVwCase.Case1
                ? VrCaseOverlaySketches[0]
                : VrCaseOverlaySketches[1],
            VrCaseOverlaySketches);
    }

    private static void ApplyOverlayLeftViewRules(
        FeaturePlanBuilder plan,
        OverlayViewConfiguration overlayView,
        bool hasVbl,
        bool hasRa2)
    {
        plan.Deactivate(TCaseOverlaySketches);

        if (overlayView != OverlayViewConfiguration.Left)
        {
            plan.ForceSuppress(TCaseOverlaySketches);
            return;
        }

        var selected = hasVbl
            ? hasRa2 ? TCaseOverlaySketches[3] : TCaseOverlaySketches[1]
            : hasRa2 ? TCaseOverlaySketches[2] : TCaseOverlaySketches[0];

        plan.ActivateOnly(selected, TCaseOverlaySketches);
    }

    private static void ApplyOverlayFootRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        FootOptionType footOption)
    {
        plan.Deactivate(VgOverlaySketch);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(VgOverlaySketch);
            return;
        }

        if (footOption == FootOptionType.Vg)
            plan.Activate(VgOverlaySketch);
    }

    private static OverlayVwCase ResolveOverlayVwCase(WedgeFacts facts)
    {
        if (!facts.HasPositive("VR") || !facts.HasPositive("VW"))
            return OverlayVwCase.None;

        if (!facts.TryGetLengthMm("VW", out var vwMm) ||
            !facts.TryGetLengthMm("W", out var wMm))
        {
            Logger.Warn(
                "[_45CKFeatureRules] VR/VW is present but W or VW is missing/not a length. " +
                "No 45CK VR overlay case was selected.");
            return OverlayVwCase.None;
        }

        return decimal.Abs(vwMm - wMm) <= WedgeFacts.DefaultPositiveEpsilon
            ? OverlayVwCase.Case1
            : OverlayVwCase.Case2;
    }

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

    private static string NormalizePackedToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

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

    private static string RemovePackedDatabaseSuffix(string raw)
    {
        var token = raw.Trim().Trim('\0');
        var separatorIndex = token.IndexOf(';');
        return separatorIndex >= 0 ? token[..separatorIndex] : token;
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
        Vg,
        Cg
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
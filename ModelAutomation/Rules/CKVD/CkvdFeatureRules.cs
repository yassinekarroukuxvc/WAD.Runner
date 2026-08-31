using System;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Common;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.ModelAutomation.Execution;
using WAD.Runner.ModelAutomation.Rules.Common;

namespace WAD.Runner.ModelAutomation.Rules.CKVD;

/// <summary>
/// CKVD feature rules.
///
/// Shank selection:
///     LW_STYLE_A_CKVD -> style_a_* family
///     LW_STYLE_B_CKVD -> style_b_* family
///
/// VR:
///     Active when VR, VW, VRR and VRA are all > 0.
///
/// Style base (style_a_feature/style_b_feature):
///     Active for the resolved shank style regardless of subclass
///     (PGB and FG both get it).
///
/// Style FR/BR family (style_a_fr_br_*/style_b_fr_*):
///     Active for the resolved shank style, FG only.
///     Suppressed for every other subclass (including PGB).
///
/// Ref point / cut:
///     ON if VR = 0 and VW = 0     -> ref_point / cut_plan_feature / cut_feature
///     ON if VR > 0 and VW > 0     -> ref_point_non_std_cut / non_std_cut_plan_feature / non_std_cut_feature
///     ref_point_a and ref_point_b are always ON for Overlay.
///     All of the above (and every overlay sketch) are suppressed
///     for non-Overlay drawings.
///
/// Overlay W:
///     w_pgb_overlay_sketch / w_fg_overlay_sketch are active for
///     their respective subclass, unless VR > 0 and VW > 0, in which
///     case a VW case sketch is used instead.
///
///     vg_fg_overlay_sketch is unconditionally ON for FG overlays.
///
///     VW Case 1 -> VR > 0 and VW > 0 and VW = W
///     VW Case 2 -> VR > 0 and VW > 0 and VW > W
///
/// Overlay style sketches:
///     PGB uses style_a_fl_pgb_overlay_sketch / style_b_fl_pgb_overlay_sketch.
///     FG uses both style_a_fr_br_overlay_sketch / style_b_fr_br_overlay_sketch
///     and style_a_fl_fg_overlay_sketch / style_b_fl_fg_overlay_sketch.
///     Selection between the A/B pair is based on the resolved shank style.
/// </summary>
public sealed class CkvdFeatureRules : IFeatureRuleSet
{
    // ================================================================
    // ALWAYS ON
    // ================================================================

    private static readonly string[] AlwaysOnNames =
    {
        "td_feature", "td_sketch",
        "isa_feature", "isa_sketch"
    };

    private static readonly string[] VrNames =
    {
        "vr_feature", "vr_sketch"
    };

    // ================================================================
    // STYLE BASE (PGB and FG)
    // ================================================================

    private static readonly string[] StyleAFeatureNames =
    {
        "style_a_feature", "style_a_sketch"
    };

    private static readonly string[] StyleBFeatureNames =
    {
        "style_b_feature", "style_b_sketch"
    };

    private static readonly string[] StyleBaseManagedNames =
    {
        "style_a_feature", "style_a_sketch",
        "style_b_feature", "style_b_sketch"
    };

    // ================================================================
    // STYLE FR/BR FAMILY (FG ONLY)
    // ================================================================

    private static readonly string[] StyleAFrBrNames =
    {
        "style_a_fr_br_feature",
        "style_a_fr_br_sketch",
        "style_a_vg_sketch",
        "style_a_fr_br_cut_feature"
    };

    private static readonly string[] StyleBFrBrNames =
    {
        "style_b_fr_feature",
        "style_b_fr_br_sketch",
        "style_b_vg_sketch",
        "style_b_fr_br_cut_feature"
    };

    private static readonly string[] StyleFrBrManagedNames =
    {
        "style_a_fr_br_feature",
        "style_a_fr_br_sketch",
        "style_a_vg_sketch",
        "style_a_fr_br_cut_feature",

        "style_b_fr_feature",
        "style_b_fr_br_sketch",
        "style_b_vg_sketch",
        "style_b_fr_br_cut_feature"
    };

    // ================================================================
    // REF POINT / CUT (OVERLAY ONLY)
    // ================================================================

    private static readonly string[] RefPointStdNames =
    {
        "ref_point",
        "cut_plan_feature",
        "cut_feature"
    };

    private static readonly string[] RefPointNonStdNames =
    {
        "ref_point_non_std_cut",
        "non_std_cut_plan_feature",
        "non_std_cut_feature"
    };

    private static readonly string[] RefPointOverlayAlwaysOnNames =
    {
        "ref_point_a",
        "ref_point_b"
    };

    private static readonly string[] RefCutManagedNames =
    {
        "ref_point",
        "cut_plan_feature",
        "cut_feature",

        "ref_point_non_std_cut",
        "non_std_cut_plan_feature",
        "non_std_cut_feature",

        "ref_point_a",
        "ref_point_b"
    };

    // ================================================================
    // OVERLAY SKETCHES (OVERLAY ONLY)
    // ================================================================

    private const string WPgbOverlaySketch =
        "w_pgb_overlay_sketch";

    private const string WFgOverlaySketch =
        "w_fg_overlay_sketch";

    private const string VgFgOverlaySketch =
        "vg_fg_overlay_sketch";

    private static readonly string[] StyleFlPgbOverlaySketches =
    {
        "style_a_fl_pgb_overlay_sketch",
        "style_b_fl_pgb_overlay_sketch"
    };

    private static readonly string[] StyleFlFgOverlaySketches =
    {
        "style_a_fl_fg_overlay_sketch",
        "style_b_fl_fg_overlay_sketch"
    };

    private static readonly string[] StyleFrBrOverlaySketches =
    {
        "style_a_fr_br_overlay_sketch",
        "style_b_fr_br_overlay_sketch"
    };

    private static readonly string[] VwCasePgbOverlaySketches =
    {
        "vw_case1_pgb_overlay_sketch",
        "vw_case2_pgb_overlay_sketch"
    };

    private static readonly string[] VwCaseFgOverlaySketches =
    {
        "vw_case1_fg_overlay_sketch",
        "vw_case2_fg_overlay_sketch"
    };

    private static readonly string[] OverlaySketchManagedNames =
    {
        "w_pgb_overlay_sketch",
        "w_fg_overlay_sketch",
        "vg_fg_overlay_sketch",

        "style_a_fl_pgb_overlay_sketch",
        "style_b_fl_pgb_overlay_sketch",

        "style_a_fl_fg_overlay_sketch",
        "style_b_fl_fg_overlay_sketch",

        "style_a_fr_br_overlay_sketch",
        "style_b_fr_br_overlay_sketch",

        "vw_case1_pgb_overlay_sketch",
        "vw_case2_pgb_overlay_sketch",

        "vw_case1_fg_overlay_sketch",
        "vw_case2_fg_overlay_sketch"
    };

    private static readonly string[] OverlayManagedNames =
    {
        "ref_point",
        "cut_plan_feature",
        "cut_feature",

        "ref_point_non_std_cut",
        "non_std_cut_plan_feature",
        "non_std_cut_feature",

        "ref_point_a",
        "ref_point_b",

        "w_pgb_overlay_sketch",
        "w_fg_overlay_sketch",
        "vg_fg_overlay_sketch",

        "style_a_fl_pgb_overlay_sketch",
        "style_b_fl_pgb_overlay_sketch",

        "style_a_fl_fg_overlay_sketch",
        "style_b_fl_fg_overlay_sketch",

        "style_a_fr_br_overlay_sketch",
        "style_b_fr_br_overlay_sketch",

        "vw_case1_pgb_overlay_sketch",
        "vw_case2_pgb_overlay_sketch",

        "vw_case1_fg_overlay_sketch",
        "vw_case2_fg_overlay_sketch"
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
            "[CkvdFeatureRules] Build -> " +
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
        CkvdShankType shank)
    {
        var isOverlay =
            context.DrawingType == DrawingType.Overlay;

        var hasVrFamily =
            HasAllPositiveNominal(
                facts,
                "VR",
                "VW",
                "VRR",
                "VRA");

        /*
         * Used for the ref-point/cut selection and the overlay W /
         * VW-case suppression. Only VR and VW are considered here,
         * unlike hasVrFamily above which also requires VRR and VRA.
         */
        var hasVrVw =
            facts.HasPositive(
                "VR") &&
            facts.HasPositive(
                "VW");

        var vwCase =
            ResolveVwCase(
                facts,
                hasVrVw);

        var plan =
            new FeaturePlanBuilder()
                .Know(AlwaysOnNames)
                .Know(VrNames)
                .Know(StyleBaseManagedNames)
                .Know(StyleFrBrManagedNames)
                .Know(OverlayManagedNames);

        ApplyBaseRules(
            plan,
            hasVrFamily);

        ApplyStyleRules(
            plan,
            context.Subclass,
            shank);

        if (isOverlay)
        {
            ApplyOverlayRules(
                plan,
                context,
                shank,
                hasVrVw,
                vwCase);
        }
        else
        {
            plan.ForceSuppress(
                OverlayManagedNames);
        }

        Logger.Info(
            "[CkvdFeatureRules] Drawing plan -> " +
            $"shank={shank}, " +
            $"drawingType={context.DrawingType}, " +
            $"subclass={context.Subclass}, " +
            $"VR family={hasVrFamily}, " +
            $"VR/VW={hasVrVw}, " +
            $"VW case={vwCase}.");

        return plan.Build();
    }

    // ================================================================
    // BASE FEATURE RULES
    // ================================================================

    private static void ApplyBaseRules(
        FeaturePlanBuilder plan,
        bool hasVrFamily)
    {
        plan.Activate(
            AlwaysOnNames);

        plan.Deactivate(
            VrNames);

        if (hasVrFamily)
        {
            plan.Activate(
                VrNames);
        }
    }

    // ================================================================
    // STYLE RULES
    // ================================================================

    private static void ApplyStyleRules(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        CkvdShankType shank)
    {
        plan.Deactivate(
            StyleBaseManagedNames);

        plan.Deactivate(
            StyleFrBrManagedNames);

        /*
         * Style base is ON for the resolved shank style regardless
         * of subclass (PGB and FG both get it).
         */
        if (shank == CkvdShankType.A)
        {
            plan.Activate(
                StyleAFeatureNames);
        }
        else
        {
            plan.Activate(
                StyleBFeatureNames);
        }

        /*
         * Style FR/BR family is FG only.
         */
        if (subclass != WedgeSubclass.FG)
        {
            plan.ForceSuppress(
                StyleFrBrManagedNames);

            Logger.Info(
                "[CkvdFeatureRules] Non-FG subclass -> style FR/BR " +
                "features suppressed.");

            return;
        }

        if (shank == CkvdShankType.A)
        {
            plan.Activate(
                StyleAFrBrNames);
        }
        else
        {
            plan.Activate(
                StyleBFrBrNames);
        }
    }

    // ================================================================
    // OVERLAY RULES
    // ================================================================

    private static void ApplyOverlayRules(
        FeaturePlanBuilder plan,
        FeatureRuleContext context,
        CkvdShankType shank,
        bool hasVrVw,
        VwCase vwCase)
    {
        plan.Deactivate(
            OverlayManagedNames);

        ApplyRefPointCutRule(
            plan,
            hasVrVw);

        ApplyWOverlayRule(
            plan,
            context.Subclass,
            hasVrVw);

        ApplyVwCaseOverlayRule(
            plan,
            context.Subclass,
            vwCase);

        ApplyStyleOverlayRule(
            plan,
            context.Subclass,
            shank);
    }

    private static void ApplyRefPointCutRule(
        FeaturePlanBuilder plan,
        bool hasVrVw)
    {
        plan.Deactivate(
            RefPointStdNames);

        plan.Deactivate(
            RefPointNonStdNames);

        if (hasVrVw)
        {
            plan.Activate(
                RefPointNonStdNames);

            plan.ForceSuppress(
                RefPointStdNames);
        }
        else
        {
            plan.Activate(
                RefPointStdNames);

            plan.ForceSuppress(
                RefPointNonStdNames);
        }

        plan.Activate(
            RefPointOverlayAlwaysOnNames);
    }

    private static void ApplyWOverlayRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        bool hasVrVw)
    {
        plan.Deactivate(
            WPgbOverlaySketch,
            WFgOverlaySketch,
            VgFgOverlaySketch);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                WFgOverlaySketch,
                VgFgOverlaySketch);

            if (hasVrVw)
            {
                plan.ForceSuppress(
                    WPgbOverlaySketch);
            }
            else
            {
                plan.Activate(
                    WPgbOverlaySketch);
            }

            return;
        }

        /*
         * FG (and any other subclass, mirroring the ABT default-to-FG
         * handling).
         */
        plan.ForceSuppress(
            WPgbOverlaySketch);

        plan.Activate(
            VgFgOverlaySketch);

        if (hasVrVw)
        {
            plan.ForceSuppress(
                WFgOverlaySketch);
        }
        else
        {
            plan.Activate(
                WFgOverlaySketch);
        }
    }

    private static void ApplyVwCaseOverlayRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        VwCase vwCase)
    {
        plan.Deactivate(
            VwCasePgbOverlaySketches);

        plan.Deactivate(
            VwCaseFgOverlaySketches);

        var targetSketches =
            subclass == WedgeSubclass.PGB
                ? VwCasePgbOverlaySketches
                : VwCaseFgOverlaySketches;

        var otherSketches =
            subclass == WedgeSubclass.PGB
                ? VwCaseFgOverlaySketches
                : VwCasePgbOverlaySketches;

        plan.ForceSuppress(
            otherSketches);

        if (vwCase == VwCase.None)
        {
            plan.ForceSuppress(
                targetSketches);

            return;
        }

        plan.ActivateOnly(
            vwCase == VwCase.Case1
                ? targetSketches[0]
                : targetSketches[1],
            targetSketches);
    }

    private static void ApplyStyleOverlayRule(
        FeaturePlanBuilder plan,
        WedgeSubclass subclass,
        CkvdShankType shank)
    {
        plan.Deactivate(
            StyleFlPgbOverlaySketches);

        plan.Deactivate(
            StyleFlFgOverlaySketches);

        plan.Deactivate(
            StyleFrBrOverlaySketches);

        if (subclass == WedgeSubclass.PGB)
        {
            plan.ForceSuppress(
                StyleFlFgOverlaySketches);

            plan.ForceSuppress(
                StyleFrBrOverlaySketches);

            plan.ActivateOnly(
                shank == CkvdShankType.A
                    ? StyleFlPgbOverlaySketches[0]
                    : StyleFlPgbOverlaySketches[1],
                StyleFlPgbOverlaySketches);

            return;
        }

        /*
         * FG (and any other subclass, mirroring the ABT default-to-FG
         * handling).
         */
        plan.ForceSuppress(
            StyleFlPgbOverlaySketches);

        plan.ActivateOnly(
            shank == CkvdShankType.A
                ? StyleFlFgOverlaySketches[0]
                : StyleFlFgOverlaySketches[1],
            StyleFlFgOverlaySketches);

        plan.ActivateOnly(
            shank == CkvdShankType.A
                ? StyleFrBrOverlaySketches[0]
                : StyleFrBrOverlaySketches[1],
            StyleFrBrOverlaySketches);
    }

    private static VwCase ResolveVwCase(
        WedgeFacts facts,
        bool hasVrVw)
    {
        if (!hasVrVw)
            return VwCase.None;

        if (!facts.TryGetLengthMm(
                "VW",
                out var vwMm) ||
            vwMm <= WedgeFacts.DefaultPositiveEpsilon)
        {
            return VwCase.None;
        }

        if (!facts.TryGetLengthMm(
                "W",
                out var wMm))
        {
            Logger.Warn(
                "[CkvdFeatureRules] VR/VW is present but W is missing " +
                "or is not a length. No CKVD VW overlay case was selected.");

            return VwCase.None;
        }

        if (decimal.Abs(
                vwMm -
                wMm) <=
            WedgeFacts.DefaultPositiveEpsilon)
        {
            return VwCase.Case1;
        }

        if (vwMm > wMm)
        {
            return VwCase.Case2;
        }

        Logger.Warn(
            "[CkvdFeatureRules] VW " +
            $"({vwMm} mm) is less than W ({wMm} mm); " +
            "no CKVD VW overlay case is defined for this combination.");

        return VwCase.None;
    }

    // ================================================================
    // SHANK SELECTION
    // ================================================================

    private static CkvdShankType ResolveShankType(
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
            "LW_STYLE_A_CKVD" or
            "STYLE_A_CKVD" or
            "STYLE_A" or
            "A" =>
                CkvdShankType.A,

            "LW_STYLE_B_CKVD" or
            "STYLE_B_CKVD" or
            "STYLE_B" or
            "B" =>
                CkvdShankType.B,

            _ =>
                throw new InvalidOperationException(
                    "Unable to resolve the CKVD shank from 'Wed-Type'. " +
                    "Expected LW_STYLE_A_CKVD or LW_STYLE_B_CKVD, but received " +
                    $"'{DisplayToken(token)}'.")
        };
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

    private enum CkvdShankType
    {
        A,
        B
    }

    private enum VwCase
    {
        None,
        Case1,
        Case2
    }
}
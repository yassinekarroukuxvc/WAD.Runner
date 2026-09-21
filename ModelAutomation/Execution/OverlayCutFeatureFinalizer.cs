using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.SolidWorks;

namespace WAD.Runner.ModelAutomation.Execution;

/// <summary>
/// Keeps overlay cut/reference feature operations out of the normal feature
/// batch and reapplies their requested state at the end.
///
/// Why this exists:
/// SolidWorks can automatically suppress an overlay cut when another feature
/// family is suppressed (for example a hole/combine family), even when the
/// cut can immediately be unsuppressed again without restoring that family.
///
/// The final pass therefore uses this order:
///
///     1. suppress cut/reference names that must be OFF
///     2. unsuppress cut/reference names that must be ON
///
/// Step 2 is intentionally the last feature operation.
/// </summary>
public static class OverlayCutFeatureFinalizer
{
    /// <summary>
    /// Overlay cut/reference names used by the current WAD wedge templates.
    ///
    /// This list is intentionally explicit. Do NOT classify every name that
    /// contains "cut", because feed-hole cuts, FR/BR cuts, and other model
    /// geometry must remain in the normal dependency-ordered feature pass.
    /// </summary>
    private static readonly HashSet<string> FinalCutFeatureNames =
        new(
            new[]
            {
                // --------------------------------------------------------
                // COB / FP / UTUS / ABT / M / 1001
                // STD shank overlay cuts
                // --------------------------------------------------------
                "std_ref_point_right",
                "std_right_cut_plan",
                "std_right_cut",

                "std_ref_point_left",
                "std_left_cut_plan",
                "std_left_cut",

                // --------------------------------------------------------
                // COB / FP / UTUS / ABT / M / 1001
                // REV shank overlay cuts
                // --------------------------------------------------------
                "rev_ref_point_right",
                "rev_right_cut_plan",
                "rev_right_cut",

                "rev_ref_point_left",
                "rev_left_cut_plan",
                "rev_left_cut",

                // --------------------------------------------------------
                // AB16 / 45CK
                // --------------------------------------------------------
                "ref_point_right",
                "right_cut_plan",
                "right_cut_feature",

                "ref_point_left",
                "left_cut_plan",
                "left_cut_feature",

                // --------------------------------------------------------
                // CKVD / OSG7
                // --------------------------------------------------------
                "ref_point",
                "ref_point_a",
                "ref_point_b",
                "cut_plan_feature",
                "cut_feature",

                // --------------------------------------------------------
                // CKVD / 4516 non-standard overlay cut
                // --------------------------------------------------------
                "ref_point_non_std_cut",
                "non_std_cut_plan_feature",
                "non_std_cut_feature",

                // --------------------------------------------------------
                // 4516 overlay references
                // --------------------------------------------------------
                "ref_point_1",
                "ref_point_2",

                // --------------------------------------------------------
                // Legacy CobLike overlay references/cuts
                // --------------------------------------------------------
                "ref_point_sketch",
                "ref_point_non_std_cut_sketch",
                "ref_point_180_DEG_REV_sketch"
            },
            StringComparer.OrdinalIgnoreCase);

    public sealed record PlanSplit(
        ModelRuleRunner.FeaturePlan NormalPlan,
        ModelRuleRunner.FeaturePlan FinalCutPlan);

    /// <summary>
    /// Splits a rule plan into:
    /// - normal features, which should execute first
    /// - overlay cut/reference features, which should execute last
    /// </summary>
    public static PlanSplit Split(
        ModelRuleRunner.FeaturePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var finalCutPlan =
            ExtractFinalCutPlan(
                plan);

        var normalPlan =
            new ModelRuleRunner.FeaturePlan(
                plan.Suppress
                    .Where(
                        name =>
                            !IsFinalCutFeature(name))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray(),

                plan.Unsuppress
                    .Where(
                        name =>
                            !IsFinalCutFeature(name))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return new PlanSplit(
            normalPlan,
            finalCutPlan);
    }

    /// <summary>
    /// Returns only the overlay cut/reference portion of a feature plan.
    /// </summary>
    public static ModelRuleRunner.FeaturePlan ExtractFinalCutPlan(
        ModelRuleRunner.FeaturePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        return new ModelRuleRunner.FeaturePlan(
            plan.Suppress
                .Where(IsFinalCutFeature)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray(),

            plan.Unsuppress
                .Where(IsFinalCutFeature)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    /// <summary>
    /// Applies the final cut/reference state.
    ///
    /// OFF features are applied first.
    /// ON features are applied second and therefore become the absolute
    /// final feature toggle operations for this pass.
    /// </summary>
    public static void ApplyLast(
        ModelEditor editor,
        ModelRuleRunner.FeaturePlan finalCutPlan,
        swInConfigurationOpts_e scope,
        string configurationName,
        string phase)
    {
        if (editor is null)
            throw new ArgumentNullException(nameof(editor));

        if (finalCutPlan is null)
            throw new ArgumentNullException(nameof(finalCutPlan));

        var suppress =
            finalCutPlan.Suppress
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var unsuppress =
            finalCutPlan.Unsuppress
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (suppress.Length == 0 &&
            unsuppress.Length == 0)
        {
            return;
        }

        Logger.Info(
            "[OverlayCutFeatureFinalizer] " +
            $"Final cut pass -> phase={phase}, " +
            $"config={configurationName}, " +
            $"suppress={suppress.Length}, " +
            $"unsuppress={unsuppress.Length}.");

        // ------------------------------------------------------------
        // FINAL CUT PHASE 1
        // Suppress all overlay cut/reference items that must be OFF.
        // ------------------------------------------------------------
        if (suppress.Length > 0)
        {
            var suppressResult =
                editor.ApplyFeatureToggles(
                    suppress,
                    Array.Empty<string>(),
                    scope);

            if (!suppressResult.IsSuccess)
            {
                Logger.Warn(
                    "[OverlayCutFeatureFinalizer] " +
                    $"Cut suppression pass completed with " +
                    $"missing={suppressResult.Missing.Count}, " +
                    $"failed={suppressResult.Failed.Count}, " +
                    $"phase={phase}, " +
                    $"config={configurationName}.");
            }
        }

        // ------------------------------------------------------------
        // FINAL CUT PHASE 2
        // Unsuppress the overlay cut/reference items that must be ON.
        //
        // This is deliberately LAST. It reproduces the manual fix where
        // a cut that SolidWorks auto-suppressed after a hole/combine
        // suppression is simply unsuppressed again afterward.
        // ------------------------------------------------------------
        if (unsuppress.Length > 0)
        {
            var unsuppressResult =
                editor.ApplyFeatureToggles(
                    Array.Empty<string>(),
                    unsuppress,
                    scope);

            if (!unsuppressResult.IsSuccess)
            {
                Logger.Warn(
                    "[OverlayCutFeatureFinalizer] " +
                    $"Cut unsuppression pass completed with " +
                    $"missing={unsuppressResult.Missing.Count}, " +
                    $"failed={unsuppressResult.Failed.Count}, " +
                    $"phase={phase}, " +
                    $"config={configurationName}.");
            }
        }
    }

    public static bool IsFinalCutFeature(
        string? featureName)
    {
        if (string.IsNullOrWhiteSpace(
                featureName))
        {
            return false;
        }

        return FinalCutFeatureNames.Contains(
            featureName.Trim());
    }
}

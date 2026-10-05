using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
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
///
/// Collateral protection:
/// Suppressing a cut/reference/plane also suppresses everything that depends
/// on it (for example other sketches), and unsuppressing does not cascade
/// back. The finalizer therefore snapshots the suppression state of every
/// feature before it starts, only touches cut/reference features that are
/// actually in the wrong state, and restores any non-cut feature that was
/// changed as a side effect.
/// </summary>
public static class OverlayCutFeatureFinalizer
{
    /// <summary>
    /// How many restore + reassert rounds to run before giving up.
    /// Restoring a collateral feature can re-trigger SolidWorks'
    /// auto-suppression of a cut, so more than one round can be needed.
    /// </summary>
    private const int MaxSettlePasses = 3;

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
    /// final feature toggle operations for this pass (unless a collateral
    /// restore had to run afterwards, in which case the cuts are verified
    /// and reasserted again).
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

        // The snapshot guard reads/writes the ACTIVE configuration only.
        // For any other scope keep the original unguarded behavior.
        if (scope != swInConfigurationOpts_e.swThisConfiguration)
        {
            ApplyUnguarded(
                editor,
                suppress,
                unsuppress,
                scope,
                configurationName,
                phase);

            return;
        }

        ApplyGuarded(
            editor,
            suppress,
            unsuppress,
            scope,
            configurationName,
            phase);
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

    // ================================================================
    // GUARDED PASS
    // ================================================================

    private static void ApplyGuarded(
        ModelEditor editor,
        string[] suppress,
        string[] unsuppress,
        swInConfigurationOpts_e scope,
        string configurationName,
        string phase)
    {
        var model = editor.Model;

        WarnIfWrongConfiguration(
            model,
            configurationName);

        // State BEFORE the finalizer touches anything. Every non-cut
        // feature is expected to still be in exactly this state afterwards.
        var snapshot =
            CaptureSnapshot(model);

        var byName =
            snapshot.ToDictionary(
                entry => entry.Name,
                StringComparer.OrdinalIgnoreCase);

        var missing =
            suppress
                .Concat(unsuppress)
                .Where(name => !byName.ContainsKey(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (missing.Length > 0)
        {
            Logger.Info(
                "[OverlayCutFeatureFinalizer] " +
                $"Not present in this model (skipped) -> " +
                $"phase={phase}, config={configurationName}: " +
                string.Join(", ", missing));
        }

        var suppressKnown =
            suppress
                .Where(byName.ContainsKey)
                .ToArray();

        var unsuppressKnown =
            unsuppress
                .Where(byName.ContainsKey)
                .ToArray();

        // ------------------------------------------------------------
        // FINAL CUT PHASE 1
        // Suppress only the OFF cuts that are not already suppressed.
        // Every unnecessary suppress is a chance to cascade.
        // ------------------------------------------------------------
        var toSuppress =
            suppressKnown
                .Where(name => !StateIs(byName[name], true))
                .ToArray();

        if (toSuppress.Length > 0)
        {
            var suppressResult =
                editor.ApplyFeatureToggles(
                    toSuppress,
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

            // Undo cascade damage BEFORE phase 2: a collateral-suppressed
            // sketch can be exactly what blocks an ON cut from coming back.
            RestoreCollateral(
                model,
                snapshot,
                configurationName,
                phase);
        }

        // ------------------------------------------------------------
        // FINAL CUT PHASE 2
        // Unsuppress the ON cuts that are not already unsuppressed.
        //
        // This reproduces the manual fix where a cut that SolidWorks
        // auto-suppressed after a hole/combine suppression is simply
        // unsuppressed again afterward.
        // ------------------------------------------------------------
        UnsuppressWrongOnCuts(
            editor,
            byName,
            unsuppressKnown,
            scope,
            configurationName,
            phase);

        // ------------------------------------------------------------
        // SETTLE LOOP
        // Restore any collateral, then confirm the cuts are still right.
        // Restoring can re-trigger auto-suppression, so re-check and
        // reassert until a clean pass (or the pass limit).
        // ------------------------------------------------------------
        for (var pass = 0; pass < MaxSettlePasses; pass++)
        {
            var restored =
                RestoreCollateral(
                    model,
                    snapshot,
                    configurationName,
                    phase);

            var offWrong =
                suppressKnown
                    .Where(name => !StateIs(byName[name], true))
                    .ToArray();

            var onWrong =
                unsuppressKnown
                    .Where(name => !StateIs(byName[name], false))
                    .ToArray();

            if (restored == 0 &&
                offWrong.Length == 0 &&
                onWrong.Length == 0)
            {
                return;
            }

            Logger.Warn(
                "[OverlayCutFeatureFinalizer] " +
                $"Settle pass {pass + 1}/{MaxSettlePasses} -> " +
                $"restored={restored}, " +
                $"offWrong={offWrong.Length}, " +
                $"onWrong={onWrong.Length}, " +
                $"phase={phase}, config={configurationName}.");

            if (offWrong.Length > 0)
            {
                editor.ApplyFeatureToggles(
                    offWrong,
                    Array.Empty<string>(),
                    scope);

                RestoreCollateral(
                    model,
                    snapshot,
                    configurationName,
                    phase);
            }

            // Keep the ON unsuppress as the last feature operation.
            UnsuppressWrongOnCuts(
                editor,
                byName,
                unsuppressKnown,
                scope,
                configurationName,
                phase);
        }

        Logger.Warn(
            "[OverlayCutFeatureFinalizer] " +
            $"Did not fully settle after {MaxSettlePasses} passes -> " +
            $"phase={phase}, config={configurationName}.");
    }

    private static void UnsuppressWrongOnCuts(
        ModelEditor editor,
        Dictionary<string, FeatureState> byName,
        string[] unsuppressKnown,
        swInConfigurationOpts_e scope,
        string configurationName,
        string phase)
    {
        var toUnsuppress =
            unsuppressKnown
                .Where(name => !StateIs(byName[name], false))
                .ToArray();

        if (toUnsuppress.Length == 0)
            return;

        var unsuppressResult =
            editor.ApplyFeatureToggles(
                Array.Empty<string>(),
                toUnsuppress,
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

    // ================================================================
    // SNAPSHOT / RESTORE
    // ================================================================

    private sealed class FeatureState
    {
        public FeatureState(
            string name,
            Feature feature,
            int order,
            bool wasSuppressed,
            bool exempt)
        {
            Name = name;
            Feature = feature;
            Order = order;
            WasSuppressed = wasSuppressed;
            Exempt = exempt;
        }

        public string Name { get; }

        public Feature Feature { get; }

        /// <summary>Position in the FeatureManager tree walk.</summary>
        public int Order { get; }

        /// <summary>Suppression state in the active configuration at snapshot time.</summary>
        public bool WasSuppressed { get; }

        /// <summary>
        /// True for final-cut features and everything nested under them.
        /// Those are allowed to change; they are never "restored".
        /// </summary>
        public bool Exempt { get; }
    }

    private static List<FeatureState> CaptureSnapshot(
        ModelDoc2 model)
    {
        var list =
            new List<FeatureState>();

        if (model is not PartDoc part)
            return list;

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var order = 0;

        var feature =
            part.FirstFeature() as Feature;

        while (feature is not null)
        {
            WalkFeature(
                feature,
                underFinalCut: false,
                list,
                seen,
                ref order);

            feature =
                feature.GetNextFeature() as Feature;
        }

        return list;
    }

    private static void WalkFeature(
        Feature feature,
        bool underFinalCut,
        List<FeatureState> list,
        HashSet<string> seen,
        ref int order)
    {
        var name =
            SafeName(feature);

        var exempt =
            underFinalCut ||
            IsFinalCutFeature(name);

        var currentOrder = order++;

        if (!string.IsNullOrWhiteSpace(name) &&
            seen.Add(name) &&
            TryReadSuppressed(feature, out var suppressed))
        {
            list.Add(
                new FeatureState(
                    name,
                    feature,
                    currentOrder,
                    suppressed,
                    exempt));
        }

        var sub =
            feature.GetFirstSubFeature() as Feature;

        while (sub is not null)
        {
            WalkFeature(
                sub,
                exempt,
                list,
                seen,
                ref order);

            sub =
                sub.GetNextSubFeature() as Feature;
        }
    }

    /// <summary>
    /// Puts every non-exempt feature back to its snapshot state.
    /// Returns how many features were successfully restored.
    /// </summary>
    private static int RestoreCollateral(
        ModelDoc2 model,
        List<FeatureState> snapshot,
        string configurationName,
        string phase)
    {
        var needUnsuppress =
            new List<FeatureState>();

        var needSuppress =
            new List<FeatureState>();

        foreach (var entry in snapshot)
        {
            if (entry.Exempt)
                continue;

            if (!TryReadSuppressed(
                    entry.Feature,
                    out var now))
            {
                continue;
            }

            if (now == entry.WasSuppressed)
                continue;

            Logger.Warn(
                "[OverlayCutFeatureFinalizer] " +
                $"Collateral change detected -> phase={phase}, " +
                $"config={configurationName}, feature='{entry.Name}', " +
                $"was={(entry.WasSuppressed ? "SUPPRESSED" : "UNSUPPRESSED")}, " +
                $"now={(now ? "SUPPRESSED" : "UNSUPPRESSED")}.");

            if (entry.WasSuppressed)
                needSuppress.Add(entry);
            else
                needUnsuppress.Add(entry);
        }

        var restored = 0;

        // Parents before children.
        foreach (var entry in
                 needUnsuppress.OrderBy(x => x.Order))
        {
            if (TrySet(entry.Feature, suppress: false))
            {
                restored++;
            }
            else
            {
                Logger.Warn(
                    "[OverlayCutFeatureFinalizer] " +
                    $"Could not restore '{entry.Name}' to UNSUPPRESSED " +
                    $"(phase={phase}, config={configurationName}).");
            }
        }

        // Children before parents.
        foreach (var entry in
                 needSuppress.OrderByDescending(x => x.Order))
        {
            if (TrySet(entry.Feature, suppress: true))
            {
                restored++;
            }
            else
            {
                Logger.Warn(
                    "[OverlayCutFeatureFinalizer] " +
                    $"Could not restore '{entry.Name}' to SUPPRESSED " +
                    $"(phase={phase}, config={configurationName}).");
            }
        }

        if (restored > 0)
        {
            // Read the true state afterward, not a stale value.
            try
            {
                model.EditRebuild3();
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "[OverlayCutFeatureFinalizer] " +
                    $"EditRebuild3 threw -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        return restored;
    }

    private static bool StateIs(
        FeatureState entry,
        bool suppressed)
    {
        // Unreadable counts as "wrong" so it gets (re)applied.
        return TryReadSuppressed(
                   entry.Feature,
                   out var now) &&
               now == suppressed;
    }

    private static bool TrySet(
        Feature feature,
        bool suppress)
    {
        try
        {
            return feature.SetSuppression2(
                suppress
                    ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                    : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration,
                null);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadSuppressed(
        Feature feature,
        out bool suppressed)
    {
        suppressed = false;

        try
        {
            var raw =
                feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);

            var values =
                new List<bool>();

            AppendSuppressionValues(
                raw,
                values);

            if (values.Count == 0)
                return false;

            suppressed =
                values.All(value => value);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendSuppressionValues(
        object? raw,
        List<bool> values)
    {
        switch (raw)
        {
            case null:
                return;

            case bool value:
                values.Add(value);
                return;

            case int value:
                values.Add(value != 0);
                return;

            case short value:
                values.Add(value != 0);
                return;

            case long value:
                values.Add(value != 0);
                return;

            case byte value:
                values.Add(value != 0);
                return;

            case Array array:
                foreach (var item in array)
                {
                    AppendSuppressionValues(
                        item,
                        values);
                }

                return;
        }
    }

    private static string SafeName(
        Feature feature)
    {
        try
        {
            return feature.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void WarnIfWrongConfiguration(
        ModelDoc2 model,
        string expectedConfigurationName)
    {
        try
        {
            var active =
                model.ConfigurationManager
                    .ActiveConfiguration?.Name;

            if (!string.IsNullOrWhiteSpace(active) &&
                !string.Equals(
                    active,
                    expectedConfigurationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn(
                    "[OverlayCutFeatureFinalizer] " +
                    $"Active configuration is '{active}' but the pass " +
                    $"was requested for '{expectedConfigurationName}'.");
            }
        }
        catch
        {
            // Diagnostic only.
        }
    }

    // ================================================================
    // UNGUARDED PASS (non-active-configuration scopes)
    // ================================================================

    private static void ApplyUnguarded(
        ModelEditor editor,
        string[] suppress,
        string[] unsuppress,
        swInConfigurationOpts_e scope,
        string configurationName,
        string phase)
    {
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
}
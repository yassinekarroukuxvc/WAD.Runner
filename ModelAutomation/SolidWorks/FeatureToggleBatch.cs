using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public sealed class FeatureToggleBatch
    {
        private readonly ModelDoc2 _model;
        private readonly IModelDocExtension _ext;
        private readonly Dictionary<string, FeatureEntry> _index;

        private static readonly Dictionary<string, string> SwTypeToSelectionType =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["ProfileFeature"] = "SKETCH",
                ["SketchBlockInst"] = "SKETCH",
                ["3DProfileFeature"] = "SKETCH",
                ["3DSketchFeature"] = "SKETCH",
                ["ReferencePoint"] = "DATUMPOINT",
                ["RefAxis"] = "AXIS",
                ["RefPlane"] = "PLANE",
                ["CoordSys"] = "COORDSYS",
                ["CurveInFile"] = "REFERENCECURVES",
                ["CompositeCurve"] = "REFERENCECURVES",
                ["3DSplineCurve"] = "REFERENCECURVES",
                ["HelixCurve"] = "REFERENCECURVES",
                ["ProjectedCurve"] = "REFERENCECURVES",
                ["IntersectCurve"] = "REFERENCECURVES",
                ["SplitBody"] = "BODYFEATURE",
                ["MoveBody"] = "BODYFEATURE",
                ["DeleteBody"] = "BODYFEATURE",
                ["CombineBodies"] = "BODYFEATURE",
            };

        private static readonly string[] SelectionProbeOrder =
        {
            "FEATURE",
            "SKETCH",
            "BODYFEATURE",
            "REFERENCECURVES",
            "DATUMPOINT",
            "AXIS",
            "PLANE",
            "COORDSYS"
        };

        private FeatureToggleBatch(
            ModelDoc2 model,
            Dictionary<string, FeatureEntry> index)
        {
            _model =
                model ??
                throw new ArgumentNullException(nameof(model));

            _ext =
                (IModelDocExtension)model.Extension;

            _index = index;
        }

        // ================================================================
        // FEATURE ENTRY
        // ================================================================

        private sealed class FeatureEntry
        {
            public Feature Feature { get; }

            /// <summary>
            /// Sub-feature nesting depth (how deeply this feature is
            /// nested inside another feature, e.g. a sketch folded into
            /// a cut). NOT the same as rebuild/tree order — kept mainly
            /// for diagnostics.
            /// </summary>
            public int Depth { get; }

            /// <summary>
            /// The position of this feature in the FeatureManager design
            /// tree walk performed during Build() (top-level features in
            /// GetNextFeature() order, sub-features interleaved via
            /// GetFirstSubFeature()/GetNextSubFeature()).
            ///
            /// This — not Depth — is the correct signal for suppress /
            /// unsuppress sequencing: a feature can only depend on
            /// features that appear earlier in the tree, regardless of
            /// how deeply either one is nested.
            /// </summary>
            public int TreeOrder { get; }

            public string? SelectionType { get; set; }

            public FeatureEntry(
                Feature feature,
                int depth,
                int treeOrder)
            {
                Feature = feature;
                Depth = depth;
                TreeOrder = treeOrder;
            }
        }

        // ================================================================
        // OPTIONS
        // ================================================================

        public sealed class ToggleOptions
        {
            /// <summary>
            /// When true, do not read the current feature state before
            /// attempting the requested toggle.
            ///
            /// Final verification is still performed afterward.
            /// </summary>
            public bool BlindApply { get; init; } = true;

            /// <summary>
            /// Uses EditSuppress2/EditUnsuppress2 for fast batches when
            /// operating on the active configuration.
            ///
            /// Every requested feature is still verified afterward.
            /// </summary>
            public bool UseSelectionBatch { get; init; } = true;

            public int BatchSize { get; init; } = 250;

            public bool ClearSelectionPerBatch { get; init; } = true;

            /// <summary>
            /// When a feature cannot be selected, use
            /// Feature.SetSuppression2 directly.
            /// </summary>
            public bool FallbackToPerFeature { get; init; } = true;

            public bool InputIsNormalized { get; init; } = false;

            /// <summary>
            /// Verify every requested feature after the fast application
            /// pass.
            ///
            /// This should normally remain enabled.
            /// </summary>
            public bool VerifyFinalState { get; init; } = true;

            /// <summary>
            /// If verification finds the wrong state, retry the feature
            /// directly using Feature.SetSuppression2.
            /// </summary>
            public bool RepairMismatches { get; init; } = true;

            /// <summary>
            /// How many verify+repair passes to run. A single mismatched
            /// feature that blocks a dependent feature can be fixed on
            /// pass 1, which then lets the dependent feature succeed on
            /// pass 2 — so more than one pass matters for batches with
            /// cross-dependencies. The loop stops early once a pass makes
            /// no further progress.
            /// </summary>
            public int MaxVerificationPasses { get; init; } = 3;

            /// <summary>
            /// Force a document rebuild after the fast apply pass and
            /// after every direct repair, before re-reading suppression
            /// state. Without this, IsSuppressed2 can return a stale
            /// value immediately after a suppression edit in the same
            /// transaction, which shows up as an intermittent,
            /// hard-to-reproduce "it says it worked but it didn't."
            /// </summary>
            public bool RebuildBeforeVerification { get; init; } = true;
        }

        // ================================================================
        // BUILD INDEX
        // ================================================================

        public static FeatureToggleBatch Build(
            ModelDoc2 model)
        {
            if (model is null)
                throw new ArgumentNullException(nameof(model));

            if (model is not PartDoc part)
            {
                throw new InvalidOperationException(
                    "FeatureToggleBatch.Build expects an opened PartDoc (SLDPRT).");
            }

            var map =
                new Dictionary<string, FeatureEntry>(
                    StringComparer.OrdinalIgnoreCase);

            var treeOrder = 0;

            var feature =
                part.FirstFeature() as Feature;

            while (feature is not null)
            {
                AddFeatureTree(
                    map,
                    feature,
                    depth: 0,
                    ref treeOrder);

                feature =
                    feature.GetNextFeature() as Feature;
            }

            Logger.Info(
                "[FeatureToggleBatch] Index built -> " +
                $"{map.Count} features (incl. sub-features).");

            return new FeatureToggleBatch(
                model,
                map);
        }

        // ================================================================
        // APPLY
        // ================================================================

        public ToggleResult Apply(
            IEnumerable<string>? suppressNames,
            IEnumerable<string>? unsuppressNames,
            swInConfigurationOpts_e scope =
                swInConfigurationOpts_e.swThisConfiguration,
            ToggleOptions? options = null,
            string? expectedActiveConfigurationName = null)
        {
            options ??=
                new ToggleOptions();

            var res =
                new ToggleResult();

            var activeConfiguration =
                GetActiveConfigurationName();

            // ============================================================
            // ACTIVE-CONFIGURATION GUARD
            // ============================================================
            //
            // Suppression state is per-configuration in SolidWorks.
            // scope=swThisConfiguration silently reads/writes whatever
            // configuration happens to be active on the model doc right
            // now. If the caller intended a specific wedge configuration
            // but the wrong one is active (a missed ShowConfiguration2,
            // a stale reference reused across configs, an async race),
            // every call below will appear to succeed while quietly
            // editing the wrong configuration's suppression state. That
            // is the single most common cause of "this feature stays
            // unsuppressed for this wedge type even though the rule
            // says suppress" bugs. Fail loudly instead of silently.
            //

            if (scope ==
                swInConfigurationOpts_e.swThisConfiguration &&
                !string.IsNullOrWhiteSpace(
                    expectedActiveConfigurationName) &&
                !string.Equals(
                    activeConfiguration,
                    expectedActiveConfigurationName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "[FeatureToggleBatch] Refusing to apply -> " +
                    $"expected active configuration '{expectedActiveConfigurationName}', " +
                    $"but the model's active configuration is '{activeConfiguration}'. " +
                    "Switch to the correct configuration (ShowConfiguration2) before " +
                    "calling Apply for this configuration's feature rules.");
            }

            var unsup =
                unsuppressNames is null
                    ? Array.Empty<string>()
                    : NormalizeInput(
                            unsuppressNames,
                            options.InputIsNormalized)
                        .ToArray();

            var sup =
                suppressNames is null
                    ? Array.Empty<string>()
                    : NormalizeInput(
                            suppressNames,
                            options.InputIsNormalized)
                        .ToArray();

            // ============================================================
            // CONFLICT RESOLUTION
            // ============================================================
            //
            // An explicit unsuppress request wins if the same feature
            // accidentally appears in both collections.
            //

            if (unsup.Length > 0 &&
                sup.Length > 0)
            {
                var unsupSet =
                    new HashSet<string>(
                        unsup,
                        StringComparer.OrdinalIgnoreCase);

                var conflicts =
                    sup
                        .Where(unsupSet.Contains)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                if (conflicts.Length > 0)
                {
                    Logger.Warn(
                        "[FeatureToggleBatch] Features requested both ON and OFF. " +
                        "ON wins -> " +
                        string.Join(", ", conflicts));
                }

                sup =
                    sup
                        .Where(x => !unsupSet.Contains(x))
                        .ToArray();
            }

            // ============================================================
            // GENERIC FEATURE DEPENDENCY ORDER
            // ============================================================
            //
            // UNSUPPRESS:
            //     earliest-in-tree -> latest-in-tree
            //
            // SUPPRESS:
            //     latest-in-tree -> earliest-in-tree
            //
            // Ordered by each feature's actual FeatureManager tree
            // position (TreeOrder), not sub-feature nesting depth — a
            // feature can only reference features that appear earlier
            // in the tree, no matter how deeply either is nested. This
            // removes the need for wedge-specific critical-name lists
            // and avoids "unsuppress attempted while a real upstream
            // dependency was still suppressed" failures.
            //

            unsup =
                OrderForTarget(
                    unsup,
                    targetSuppress: false);

            sup =
                OrderForTarget(
                    sup,
                    targetSuppress: true);

            Logger.Info(
                "[FeatureToggleBatch] Apply -> " +
                $"config={activeConfiguration}, " +
                $"scope={scope}, " +
                $"unsup={unsup.Length}, " +
                $"sup={sup.Length}, " +
                $"batch={options.UseSelectionBatch}, " +
                $"blind={options.BlindApply}, " +
                $"verify={options.VerifyFinalState}, " +
                $"repair={options.RepairMismatches}.");

            // ============================================================
            // FAST APPLICATION PASS
            // ============================================================

            if (scope ==
                swInConfigurationOpts_e.swThisConfiguration &&
                options.UseSelectionBatch)
            {
                ApplyBySelectionBatches(
                    unsup,
                    suppress: false,
                    scope,
                    options,
                    res);

                ApplyBySelectionBatches(
                    sup,
                    suppress: true,
                    scope,
                    options,
                    res);
            }
            else
            {
                //
                // SetSuppression2 is used directly when we are not simply
                // editing the currently-active configuration.
                //

                foreach (var name in unsup)
                {
                    ToggleOnePerFeature(
                        name,
                        targetSuppress: false,
                        scope,
                        blindApply: options.BlindApply,
                        res);
                }

                foreach (var name in sup)
                {
                    ToggleOnePerFeature(
                        name,
                        targetSuppress: true,
                        scope,
                        blindApply: options.BlindApply,
                        res);
                }
            }

            // ============================================================
            // FINAL VERIFICATION + REPAIR
            // ============================================================
            //
            // EditSuppress2/EditUnsuppress2/SetSuppression2 returning
            // true does NOT prove every selected feature actually
            // reached the requested state, and reading the state back
            // immediately can be stale until a rebuild runs. This pass
            // makes the requested feature plan the source of truth, and
            // runs multiple passes because repairing one feature can
            // unblock a dependent feature that failed earlier in the
            // same run.
            //

            if (options.VerifyFinalState)
            {
                if (options.RebuildBeforeVerification)
                {
                    ForceRebuild();
                }

                var passes =
                    Math.Max(
                        1,
                        options.MaxVerificationPasses);

                for (var pass = 0;
                     pass < passes;
                     pass++)
                {
                    var failedBefore =
                        res.Failed.Count;

                    VerifyAndRepairRequestedState(
                        unsup,
                        targetSuppress: false,
                        scope,
                        options,
                        res);

                    VerifyAndRepairRequestedState(
                        sup,
                        targetSuppress: true,
                        scope,
                        options,
                        res);

                    Logger.Info(
                        "[FeatureToggleBatch] Verification pass " +
                        $"{pass + 1}/{passes} -> failed={res.Failed.Count}.");

                    if (res.Failed.Count == 0)
                        break;

                    //
                    // No progress this pass -> further passes won't help
                    // either; stop instead of spinning.
                    //

                    if (res.Failed.Count == failedBefore)
                        break;
                }
            }

            Logger.Info(
                "[FeatureToggleBatch] Apply done -> " +
                $"config={activeConfiguration}, " +
                $"unsuppressed={res.Unsuppressed.Count}, " +
                $"suppressed={res.Suppressed.Count}, " +
                $"skipped={res.SkippedAlreadyCorrect.Count}, " +
                $"missing={res.Missing.Count}, " +
                $"failed={res.Failed.Count}");

            if (res.Missing.Count > 0)
            {
                Logger.Warn(
                    "[FeatureToggleBatch] Missing -> " +
                    string.Join(
                        ", ",
                        res.Missing.Take(50)));
            }

            if (res.Failed.Count > 0)
            {
                Logger.Warn(
                    "[FeatureToggleBatch] Failed -> " +
                    string.Join(
                        ", ",
                        res.Failed
                            .Take(50)
                            .Select(
                                kv =>
                                    $"{kv.Key} => {kv.Value}")));
            }

            return res;
        }

        // ================================================================
        // SINGLE TOGGLE
        // ================================================================

        public bool TryToggle(
            string featureName,
            bool suppress,
            swInConfigurationOpts_e scope =
                swInConfigurationOpts_e.swThisConfiguration)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                return false;

            var name =
                featureName.Trim();

            if (!_index.TryGetValue(
                    name,
                    out var entry))
            {
                return false;
            }

            if (TryReadMatchesTarget(
                    entry,
                    scope,
                    suppress,
                    out var alreadyCorrect,
                    out _) &&
                alreadyCorrect)
            {
                return true;
            }

            if (!TrySet(
                    entry,
                    suppress,
                    scope,
                    out _))
            {
                return false;
            }

            ForceRebuild();

            if (!TryReadMatchesTarget(
                    entry,
                    scope,
                    suppress,
                    out var finalCorrect,
                    out _))
            {
                return false;
            }

            return finalCorrect;
        }

        // ================================================================
        // GENERIC ORDERING
        // ================================================================

        private string[] OrderForTarget(
            string[] names,
            bool targetSuppress)
        {
            if (names.Length == 0)
                return names;

            var entries =
                names
                    .Select(
                        (name, originalIndex) =>
                        {
                            var known =
                                _index.TryGetValue(
                                    name,
                                    out var entry);

                            return new
                            {
                                Name = name,
                                OriginalIndex = originalIndex,
                                Known = known,
                                TreeOrder = known
                                    ? entry!.TreeOrder
                                    : 0
                            };
                        });

            //
            // Known features first.
            //
            // Suppress:
            //     latest-in-tree first (so descendants go before the
            //     ancestors they depend on).
            //
            // Unsuppress:
            //     earliest-in-tree first (parents before children).
            //

            return entries
                .OrderBy(x => x.Known ? 0 : 1)
                .ThenBy(
                    x =>
                        targetSuppress
                            ? -x.TreeOrder
                            : x.TreeOrder)
                .ThenBy(x => x.OriginalIndex)
                .Select(x => x.Name)
                .ToArray();
        }

        // ================================================================
        // BATCH SELECTION APPLICATION
        // ================================================================

        private void ApplyBySelectionBatches(
            string[] names,
            bool suppress,
            swInConfigurationOpts_e scope,
            ToggleOptions options,
            ToggleResult res)
        {
            if (names.Length == 0)
                return;

            var batchSize =
                Math.Max(
                    1,
                    options.BatchSize);

            for (var i = 0;
                 i < names.Length;
                 i += batchSize)
            {
                var count =
                    Math.Min(
                        batchSize,
                        names.Length - i);

                if (options.ClearSelectionPerBatch)
                    _model.ClearSelection2(true);

                var selected =
                    new List<string>(
                        count);

                var perFeatureFallback =
                    new List<string>();

                for (var j = 0;
                     j < count;
                     j++)
                {
                    var name =
                        names[i + j];

                    if (!options.BlindApply &&
                        _index.TryGetValue(
                            name,
                            out var currentEntry))
                    {
                        if (TryReadMatchesTarget(
                                currentEntry,
                                scope,
                                suppress,
                                out var alreadyCorrect,
                                out _) &&
                            alreadyCorrect)
                        {
                            MarkSkipped(
                                res,
                                name);

                            continue;
                        }
                    }

                    if (TrySelectByNameHeuristics(
                            name,
                            append: true))
                    {
                        selected.Add(name);
                        continue;
                    }

                    //
                    // Feature exists but could not be selected.
                    //
                    // This commonly happens for suppressed, absorbed,
                    // nested, or parent-dependent features.
                    //

                    if (_index.ContainsKey(name))
                    {
                        if (options.FallbackToPerFeature)
                        {
                            perFeatureFallback.Add(
                                name);
                        }
                        else
                        {
                            MarkFailure(
                                res,
                                name,
                                "Feature exists in the index, but SelectByID2 could not select it.");
                        }

                        continue;
                    }

                    MarkMissing(
                        res,
                        name);
                }

                if (selected.Count > 0)
                {
                    if (!TrySetSelectionSuppression(
                            suppress,
                            out var error))
                    {
                        if (options.FallbackToPerFeature)
                        {
                            foreach (var name in selected)
                            {
                                ToggleOnePerFeature(
                                    name,
                                    targetSuppress: suppress,
                                    scope,
                                    blindApply: true,
                                    res,
                                    forceApply: true);
                            }
                        }
                        else
                        {
                            foreach (var name in selected)
                            {
                                MarkFailure(
                                    res,
                                    name,
                                    error);
                            }
                        }
                    }
                    else
                    {
                        //
                        // This is only the command result.
                        //
                        // Final verification below will prove whether each
                        // feature actually reached this state.
                        //

                        foreach (var name in selected)
                        {
                            MarkCommandSuccess(
                                res,
                                name,
                                suppress);
                        }
                    }
                }

                foreach (var name in perFeatureFallback)
                {
                    ToggleOnePerFeature(
                        name,
                        targetSuppress: suppress,
                        scope,
                        blindApply: true,
                        res,
                        forceApply: true);
                }

                //
                // Never let one batch's selections leak into the next
                // suppression operation.
                //

                _model.ClearSelection2(true);
            }
        }

        // ================================================================
        // VERIFY + REPAIR EVERY REQUESTED FEATURE
        // ================================================================

        private void VerifyAndRepairRequestedState(
            IEnumerable<string> names,
            bool targetSuppress,
            swInConfigurationOpts_e scope,
            ToggleOptions options,
            ToggleResult res)
        {
            var activeConfiguration =
                GetActiveConfigurationName();

            foreach (var name in
                     names.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!_index.TryGetValue(
                        name,
                        out var entry))
                {
                    MarkMissing(
                        res,
                        name);

                    continue;
                }

                if (TryReadMatchesTarget(
                        entry,
                        scope,
                        targetSuppress,
                        out var correct,
                        out var actualDescription) &&
                    correct)
                {
                    MarkVerifiedCorrect(
                        res,
                        name,
                        targetSuppress);

                    Logger.Info(
                        "[FeatureToggleBatch] Verified -> " +
                        $"config={activeConfiguration}, " +
                        $"feature='{name}', " +
                        $"state={TargetStateName(targetSuppress)}.");

                    continue;
                }

                Logger.Warn(
                    "[FeatureToggleBatch] Verification mismatch -> " +
                    $"config={activeConfiguration}, " +
                    $"feature='{name}', " +
                    $"wanted={TargetStateName(targetSuppress)}, " +
                    $"actual={actualDescription}.");

                if (!options.RepairMismatches)
                {
                    MarkFailure(
                        res,
                        name,
                        $"Verification mismatch. Wanted {TargetStateName(targetSuppress)}, actual={actualDescription}.");

                    continue;
                }

                // ========================================================
                // DIRECT REPAIR
                // ========================================================

                if (!TrySet(
                        entry,
                        targetSuppress,
                        scope,
                        out var repairError))
                {
                    MarkFailure(
                        res,
                        name,
                        "Direct SetSuppression2 repair failed: " +
                        repairError);

                    Logger.Error(
                        "[FeatureToggleBatch] Repair failed -> " +
                        $"config={activeConfiguration}, " +
                        $"feature='{name}', " +
                        $"wanted={TargetStateName(targetSuppress)}, " +
                        $"error={repairError}");

                    continue;
                }

                if (options.RebuildBeforeVerification)
                {
                    //
                    // Read the repaired state only after a rebuild, or
                    // this check can see the pre-repair value and either
                    // falsely fail a repair that actually worked, or
                    // falsely pass one that didn't.
                    //

                    ForceRebuild();
                }

                // ========================================================
                // VERIFY REPAIR
                // ========================================================

                if (!TryReadMatchesTarget(
                        entry,
                        scope,
                        targetSuppress,
                        out var repairedCorrect,
                        out var repairedDescription))
                {
                    MarkFailure(
                        res,
                        name,
                        "Unable to verify feature state after SetSuppression2 repair.");

                    Logger.Error(
                        "[FeatureToggleBatch] Repair verification failed -> " +
                        $"config={activeConfiguration}, " +
                        $"feature='{name}'.");

                    continue;
                }

                if (!repairedCorrect)
                {
                    MarkFailure(
                        res,
                        name,
                        "Feature remained in the wrong state after SetSuppression2 repair. " +
                        $"Wanted {TargetStateName(targetSuppress)}, actual={repairedDescription}.");

                    Logger.Error(
                        "[FeatureToggleBatch] Repair did not stick -> " +
                        $"config={activeConfiguration}, " +
                        $"feature='{name}', " +
                        $"wanted={TargetStateName(targetSuppress)}, " +
                        $"actual={repairedDescription}.");

                    continue;
                }

                MarkSuccess(
                    res,
                    name,
                    targetSuppress);

                Logger.Info(
                    "[FeatureToggleBatch] Repair succeeded -> " +
                    $"config={activeConfiguration}, " +
                    $"feature='{name}', " +
                    $"state={TargetStateName(targetSuppress)}.");
            }
        }

        // ================================================================
        // REBUILD
        // ================================================================

        private void ForceRebuild()
        {
            try
            {
                _model.EditRebuild3();
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    "[FeatureToggleBatch] EditRebuild3 threw -> " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ================================================================
        // SELECTION SUPPRESSION
        // ================================================================

        private bool TrySetSelectionSuppression(
            bool suppress,
            out string error)
        {
            error =
                string.Empty;

            try
            {
                var ok =
                    suppress
                        ? _model.EditSuppress2()
                        : _model.EditUnsuppress2();

                if (!ok)
                {
                    error =
                        suppress
                            ? "EditSuppress2 returned false."
                            : "EditUnsuppress2 returned false.";

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error =
                    $"{ex.GetType().Name}: {ex.Message}";

                return false;
            }
        }

        // ================================================================
        // SELECT FEATURE
        // ================================================================

        private bool TrySelectByNameHeuristics(
            string name,
            bool append)
        {
            const int mark = 0;
            const double x = 0;
            const double y = 0;
            const double z = 0;

            try
            {
                if (_index.TryGetValue(
                        name,
                        out var entry) &&
                    !string.IsNullOrWhiteSpace(
                        entry.SelectionType))
                {
                    if (_ext.SelectByID2(
                            name,
                            entry.SelectionType,
                            x,
                            y,
                            z,
                            append,
                            mark,
                            null,
                            0))
                    {
                        return true;
                    }
                }

                foreach (var type in
                         SelectionProbeOrder)
                {
                    if (!_ext.SelectByID2(
                            name,
                            type,
                            x,
                            y,
                            z,
                            append,
                            mark,
                            null,
                            0))
                    {
                        continue;
                    }

                    if (_index.TryGetValue(
                            name,
                            out var resolvedEntry))
                    {
                        resolvedEntry.SelectionType =
                            type;
                    }

                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ================================================================
        // PER-FEATURE TOGGLE
        // ================================================================

        private void ToggleOnePerFeature(
            string name,
            bool targetSuppress,
            swInConfigurationOpts_e scope,
            bool blindApply,
            ToggleResult res,
            bool forceApply = false)
        {
            if (!_index.TryGetValue(
                    name,
                    out var entry))
            {
                MarkMissing(
                    res,
                    name);

                return;
            }

            if (!forceApply &&
                !blindApply)
            {
                if (TryReadMatchesTarget(
                        entry,
                        scope,
                        targetSuppress,
                        out var alreadyCorrect,
                        out _) &&
                    alreadyCorrect)
                {
                    MarkSkipped(
                        res,
                        name);

                    return;
                }
            }

            if (TrySet(
                    entry,
                    targetSuppress,
                    scope,
                    out var error))
            {
                MarkCommandSuccess(
                    res,
                    name,
                    targetSuppress);

                return;
            }

            MarkFailure(
                res,
                name,
                error);
        }

        // ================================================================
        // SETSUPPRESSION2
        // ================================================================

        private static bool TrySet(
            FeatureEntry entry,
            bool suppress,
            swInConfigurationOpts_e scope,
            out string error)
        {
            error =
                string.Empty;

            try
            {
                var ok =
                    entry.Feature.SetSuppression2(
                        suppress
                            ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                            : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                        (int)scope,
                        null);

                if (!ok)
                {
                    error =
                        suppress
                            ? "SetSuppression2 returned false (suppress)."
                            : "SetSuppression2 returned false (unsuppress).";

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error =
                    $"{ex.GetType().Name}: {ex.Message}";

                return false;
            }
        }

        // ================================================================
        // READ / VERIFY SUPPRESSION STATE
        // ================================================================

        private static bool TryReadMatchesTarget(
            FeatureEntry entry,
            swInConfigurationOpts_e scope,
            bool targetSuppress,
            out bool matches,
            out string actualDescription)
        {
            matches =
                false;

            actualDescription =
                "<unable-to-read>";

            if (!TryReadSuppressionStates(
                    entry,
                    scope,
                    out var states))
            {
                return false;
            }

            matches =
                states.All(
                    value =>
                        value == targetSuppress);

            actualDescription =
                DescribeSuppressionStates(
                    states);

            return true;
        }

        private static bool TryReadSuppressionStates(
            FeatureEntry entry,
            swInConfigurationOpts_e scope,
            out bool[] states)
        {
            states =
                Array.Empty<bool>();

            try
            {
                var raw =
                    entry.Feature.IsSuppressed2(
                        (int)scope,
                        null);

                return TryDecodeSuppressionVariant(
                    raw,
                    out states);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeSuppressionVariant(
            object? raw,
            out bool[] states)
        {
            var values =
                new List<bool>();

            AppendSuppressionValues(
                raw,
                values);

            states =
                values.ToArray();

            return states.Length > 0;
        }

        private static void AppendSuppressionValues(
            object? raw,
            List<bool> values)
        {
            if (raw is null)
                return;

            switch (raw)
            {
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

                case sbyte value:
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

        private static string DescribeSuppressionStates(
            IReadOnlyCollection<bool> states)
        {
            if (states.Count == 0)
                return "<empty>";

            if (states.All(x => x))
                return "SUPPRESSED";

            if (states.All(x => !x))
                return "UNSUPPRESSED";

            var suppressed =
                states.Count(x => x);

            var unsuppressed =
                states.Count - suppressed;

            return
                $"MIXED(suppressed={suppressed}, unsuppressed={unsuppressed})";
        }

        // ================================================================
        // FEATURE TREE INDEX
        // ================================================================

        private static void AddFeatureTree(
            Dictionary<string, FeatureEntry> map,
            Feature feature,
            int depth,
            ref int treeOrder)
        {
            TryAdd(
                map,
                feature,
                depth,
                ref treeOrder);

            var subFeature =
                feature.GetFirstSubFeature()
                    as Feature;

            while (subFeature is not null)
            {
                AddFeatureTree(
                    map,
                    subFeature,
                    depth + 1,
                    ref treeOrder);

                subFeature =
                    subFeature.GetNextSubFeature()
                        as Feature;
            }
        }

        private static void TryAdd(
            Dictionary<string, FeatureEntry> map,
            Feature feature,
            int depth,
            ref int treeOrder)
        {
            try
            {
                var name =
                    feature.Name;

                if (string.IsNullOrWhiteSpace(name))
                    return;

                if (map.ContainsKey(name))
                    return;

                var entry =
                    new FeatureEntry(
                        feature,
                        depth,
                        treeOrder++);

                var swType =
                    feature.GetTypeName2()
                        as string ??
                    string.Empty;

                entry.SelectionType =
                    SwTypeToSelectionType.TryGetValue(
                        swType,
                        out var selectionType)
                        ? selectionType
                        : "FEATURE";

                map.Add(
                    name,
                    entry);
            }
            catch
            {
            }
        }

        // ================================================================
        // INPUT NORMALIZATION
        // ================================================================

        private static IEnumerable<string> NormalizeInput(
            IEnumerable<string> names,
            bool alreadyNormalized)
        {
            var normalized =
                alreadyNormalized
                    ? NormalizeFast(names)
                    : Normalize(names);

            //
            // Always deduplicate.
            //
            // Duplicate feature requests provide no benefit and make
            // verification/results harder to reason about.
            //

            return normalized
                .Distinct(
                    StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> NormalizeFast(
            IEnumerable<string> names)
        {
            return names
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(value))
                .Select(
                    value =>
                        value.Trim());
        }

        private static IEnumerable<string> Normalize(
            IEnumerable<string> names)
        {
            return names
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(value))
                .Select(
                    value =>
                        value.Trim());
        }

        // ================================================================
        // RESULT MANAGEMENT
        // ================================================================

        private static void MarkCommandSuccess(
            ToggleResult res,
            string name,
            bool suppressed)
        {
            res.Failed.Remove(name);

            RemoveName(
                res.Missing,
                name);

            if (suppressed)
            {
                RemoveName(
                    res.Unsuppressed,
                    name);

                AddUnique(
                    res.Suppressed,
                    name);
            }
            else
            {
                RemoveName(
                    res.Suppressed,
                    name);

                AddUnique(
                    res.Unsuppressed,
                    name);
            }
        }

        private static void MarkVerifiedCorrect(
            ToggleResult res,
            string name,
            bool suppressed)
        {
            res.Failed.Remove(name);

            RemoveName(
                res.Missing,
                name);

            //
            // Preserve the "already correct" classification when the
            // operation was skipped because the feature started in the
            // requested state.
            //

            if (ContainsName(
                    res.SkippedAlreadyCorrect,
                    name))
            {
                return;
            }

            MarkSuccess(
                res,
                name,
                suppressed);
        }

        private static void MarkSuccess(
            ToggleResult res,
            string name,
            bool suppressed)
        {
            res.Failed.Remove(name);

            RemoveName(
                res.Missing,
                name);

            RemoveName(
                res.SkippedAlreadyCorrect,
                name);

            if (suppressed)
            {
                RemoveName(
                    res.Unsuppressed,
                    name);

                AddUnique(
                    res.Suppressed,
                    name);
            }
            else
            {
                RemoveName(
                    res.Suppressed,
                    name);

                AddUnique(
                    res.Unsuppressed,
                    name);
            }
        }

        private static void MarkSkipped(
            ToggleResult res,
            string name)
        {
            res.Failed.Remove(name);

            RemoveName(
                res.Missing,
                name);

            RemoveName(
                res.Suppressed,
                name);

            RemoveName(
                res.Unsuppressed,
                name);

            AddUnique(
                res.SkippedAlreadyCorrect,
                name);
        }

        private static void MarkMissing(
            ToggleResult res,
            string name)
        {
            res.Failed.Remove(name);

            RemoveName(
                res.Suppressed,
                name);

            RemoveName(
                res.Unsuppressed,
                name);

            RemoveName(
                res.SkippedAlreadyCorrect,
                name);

            AddUnique(
                res.Missing,
                name);
        }

        private static void MarkFailure(
            ToggleResult res,
            string name,
            string error)
        {
            RemoveName(
                res.Suppressed,
                name);

            RemoveName(
                res.Unsuppressed,
                name);

            RemoveName(
                res.SkippedAlreadyCorrect,
                name);

            RemoveName(
                res.Missing,
                name);

            res.Failed[name] =
                error;
        }

        private static void AddUnique(
            List<string> values,
            string name)
        {
            if (!ContainsName(
                    values,
                    name))
            {
                values.Add(name);
            }
        }

        private static bool ContainsName(
            IEnumerable<string> values,
            string name)
        {
            return values.Contains(
                name,
                StringComparer.OrdinalIgnoreCase);
        }

        private static void RemoveName(
            List<string> values,
            string name)
        {
            values.RemoveAll(
                value =>
                    string.Equals(
                        value,
                        name,
                        StringComparison.OrdinalIgnoreCase));
        }

        // ================================================================
        // LOGGING HELPERS
        // ================================================================

        private string GetActiveConfigurationName()
        {
            try
            {
                var configurationManager =
                    _model.ConfigurationManager;

                var configuration =
                    configurationManager.ActiveConfiguration;

                return configuration?.Name ??
                       "<unknown>";
            }
            catch
            {
                return "<unknown>";
            }
        }

        private static string TargetStateName(
            bool suppress)
        {
            return suppress
                ? "SUPPRESSED"
                : "UNSUPPRESSED";
        }

        // ================================================================
        // RESULT
        // ================================================================

        public sealed class ToggleResult
        {
            public List<string> Suppressed { get; } =
                new();

            public List<string> Unsuppressed { get; } =
                new();

            public List<string> SkippedAlreadyCorrect { get; } =
                new();

            public List<string> Missing { get; } =
                new();

            public Dictionary<string, string> Failed { get; } =
                new(
                    StringComparer.OrdinalIgnoreCase);

            public bool IsSuccess =>
                Missing.Count == 0 &&
                Failed.Count == 0;
        }
    }
}
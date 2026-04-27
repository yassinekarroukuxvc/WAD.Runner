// ModelAutomation/SolidWorks/FeatureToggleBatch.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    /// <summary>
    /// Fast, macro-style feature suppression/unsuppression.
    ///
    /// TWO execution paths — chosen automatically based on scope:
    ///
    ///   swThisConfiguration  →  Selection-batch fast path.
    ///     Selects features in groups, then calls EditSuppress2 / EditUnsuppress2.
    ///     Very fast (fewer COM round-trips) but ONLY affects the active configuration
    ///     because EditSuppress2/EditUnsuppress2 have no scope parameter.
    ///
    ///   swAllConfiguration   →  Per-feature SetSuppression2 path.
    ///     Calls Feature.SetSuppression2(..., swAllConfiguration, ...) on every feature
    ///     individually. Slower, but this is the ONLY SW API that actually writes the
    ///     suppression state across all configurations. EditSuppress2/EditUnsuppress2
    ///     silently ignore scope — using them for AllConfiguration is a SW API limitation.
    ///
    /// IMPORTANT: No rebuilds here. Orchestrator owns the single rebuild at the end.
    /// </summary>
    public sealed class FeatureToggleBatch
    {
        private readonly ModelDoc2 _model;
        private readonly IModelDocExtension _ext;
        private readonly Dictionary<string, FeatureEntry> _index;

        // Some overlay cut items have parent/child dependencies in the feature tree.
        // In particular, cut_feature can remain suppressed when it is unsuppressed
        // in the same selection batch as cut_plan_feature.
        //
        // Important:
        //   - For swThisConfiguration, apply these one-by-one using selection +
        //     EditSuppress2/EditUnsuppress2. That preserves different states in
        //     std_cut vs non_std_cut.
        //   - For swAllConfiguration, use SetSuppression2 because that is the only
        //     API that can intentionally write all configurations.
        private static readonly string[] CriticalUnsuppressOrder =
        {
            "cut_plan_feature",
            "cut_feature",
            "non_std_cut_plan_feature",
            "non_std_cut_feature"
        };

        private static readonly string[] CriticalSuppressOrder =
        {
            "cut_feature",
            "cut_plan_feature",
            "non_std_cut_feature",
            "non_std_cut_plan_feature"
        };

        private FeatureToggleBatch(ModelDoc2 model, Dictionary<string, FeatureEntry> index)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _ext = (IModelDocExtension)model.Extension;
            _index = index;
        }

        private sealed class FeatureEntry
        {
            public Feature Feature { get; }
            public bool? IsSuppressedCached { get; set; }

            public FeatureEntry(Feature feature)
            {
                Feature = feature;
            }
        }

        public sealed class ToggleOptions
        {
            /// <summary>
            /// If true, skip IsSuppressed2 reads.
            /// Default: true.
            /// </summary>
            public bool BlindApply { get; init; } = true;

            /// <summary>
            /// Use selection-batch suppression for ThisConfiguration jobs.
            /// Ignored when scope == swAllConfiguration.
            /// Default: true.
            /// </summary>
            public bool UseSelectionBatch { get; init; } = true;

            /// <summary>
            /// How many items to select per batch.
            /// Default: 80.
            /// </summary>
            public int BatchSize { get; init; } = 80;

            /// <summary>
            /// Clear selection before each batch.
            /// Default: true.
            /// </summary>
            public bool ClearSelectionPerBatch { get; init; } = true;

            /// <summary>
            /// When selection-batch fails for a name, fallback to per-feature SetSuppression2.
            /// Default: true.
            /// </summary>
            public bool FallbackToPerFeature { get; init; } = true;
        }

        public static FeatureToggleBatch Build(ModelDoc2 model)
        {
            if (model is null)
                throw new ArgumentNullException(nameof(model));

            if (model is not PartDoc part)
                throw new InvalidOperationException("FeatureToggleBatch.Build expects an opened PartDoc (SLDPRT).");

            var map = new Dictionary<string, FeatureEntry>(StringComparer.OrdinalIgnoreCase);

            var f = (Feature)part.FirstFeature();
            while (f != null)
            {
                TryAdd(map, f);

                var sub = (Feature)f.GetFirstSubFeature();
                while (sub != null)
                {
                    TryAdd(map, sub);
                    sub = (Feature)sub.GetNextSubFeature();
                }

                f = (Feature)f.GetNextFeature();
            }

            Logger.Info($"[FeatureToggleBatch] Index built → {map.Count} features (incl. sub-features).");
            return new FeatureToggleBatch(model, map);
        }

        public ToggleResult Apply(
            IEnumerable<string>? suppressNames,
            IEnumerable<string>? unsuppressNames,
            swInConfigurationOpts_e scope = swInConfigurationOpts_e.swThisConfiguration,
            ToggleOptions? options = null)
        {
            options ??= new ToggleOptions();

            var res = new ToggleResult();

            foreach (var e in _index.Values)
                e.IsSuppressedCached = null;

            var unsup = unsuppressNames is null
                ? Array.Empty<string>()
                : Normalize(unsuppressNames).ToArray();

            var sup = suppressNames is null
                ? Array.Empty<string>()
                : Normalize(suppressNames).ToArray();

            // Unsuppress wins when a name appears in both lists.
            if (unsup.Length > 0 && sup.Length > 0)
                sup = sup.Where(s => !unsup.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

            var criticalUnsup = ExtractOrdered(unsup, CriticalUnsuppressOrder);
            var criticalSup = ExtractOrdered(sup, CriticalSuppressOrder);

            unsup = RemoveNames(unsup, criticalUnsup);
            sup = RemoveNames(sup, criticalSup);

            Logger.Info(
                $"[FeatureToggleBatch] Apply(scope={scope}) " +
                $"unsup={unsup.Length}+critical({criticalUnsup.Length}), " +
                $"sup={sup.Length}+critical({criticalSup.Length}), " +
                $"batch={options.UseSelectionBatch}, blind={options.BlindApply}");

            if (scope == swInConfigurationOpts_e.swAllConfiguration)
            {
                Logger.Info("[FeatureToggleBatch] scope=AllConfiguration → per-feature path (SetSuppression2).");

                ApplyCriticalInOrder(criticalUnsup, suppress: false, scope, res);

                foreach (var name in unsup)
                    ToggleOnePerFeature(name, targetSuppress: false, scope, blindApply: true, res);

                foreach (var name in sup)
                    ToggleOnePerFeature(name, targetSuppress: true, scope, blindApply: true, res);

                ApplyCriticalInOrder(criticalSup, suppress: true, scope, res);
            }
            else if (options.UseSelectionBatch)
            {
                // Unsuppress parent/child cut features first, outside the selection batch.
                ApplyCriticalInOrder(criticalUnsup, suppress: false, scope, res);

                ApplyBySelectionBatches(unsup, suppress: false, scope, options, res);
                ApplyBySelectionBatches(sup, suppress: true, scope, options, res);

                // Suppress dependent cut features last, child before parent.
                ApplyCriticalInOrder(criticalSup, suppress: true, scope, res);
            }
            else
            {
                ApplyCriticalInOrder(criticalUnsup, suppress: false, scope, res);

                foreach (var name in unsup)
                    ToggleOnePerFeature(name, targetSuppress: false, scope, options.BlindApply, res);

                foreach (var name in sup)
                    ToggleOnePerFeature(name, targetSuppress: true, scope, options.BlindApply, res);

                ApplyCriticalInOrder(criticalSup, suppress: true, scope, res);
            }

            VerifyCriticalUnsuppressed(criticalUnsup, scope, res);

            Logger.Info(
                "[FeatureToggleBatch] Apply done → " +
                $"unsuppressed={res.Unsuppressed.Count}, suppressed={res.Suppressed.Count}, " +
                $"skipped={res.SkippedAlreadyCorrect.Count}, missing={res.Missing.Count}, failed={res.Failed.Count}");

            if (res.Missing.Count > 0)
                Logger.Warn("[FeatureToggleBatch] Missing: " + string.Join(", ", res.Missing.Take(50)));

            if (res.Failed.Count > 0)
            {
                Logger.Warn(
                    "[FeatureToggleBatch] Failed: " +
                    string.Join(", ", res.Failed.Take(20).Select(kv => $"{kv.Key} => {kv.Value}")));
            }

            return res;
        }

        public bool TryToggle(
            string featureName,
            bool suppress,
            swInConfigurationOpts_e scope = swInConfigurationOpts_e.swThisConfiguration)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                return false;

            var name = featureName.Trim();

            if (!_index.TryGetValue(name, out var entry))
                return false;

            if (TryGetIsSuppressed(entry, scope, out var isSuppressed) && isSuppressed == suppress)
                return true;

            if (!TrySet(entry, suppress, scope, out _))
                return false;

            entry.IsSuppressedCached = suppress;
            return true;
        }

        private void ApplyCriticalInOrder(
            string[] names,
            bool suppress,
            swInConfigurationOpts_e scope,
            ToggleResult res)
        {
            if (names.Length == 0)
                return;

            Logger.Info(
                $"[FeatureToggleBatch] Critical ordered {(suppress ? "suppress" : "unsuppress")} → " +
                string.Join(", ", names));

            foreach (var name in names)
                ToggleOneCritical(name, suppress, scope, res);
        }

        private void ToggleOneCritical(
            string name,
            bool suppress,
            swInConfigurationOpts_e scope,
            ToggleResult res)
        {
            // For all-config jobs, SetSuppression2 is intentional and required.
            if (scope == swInConfigurationOpts_e.swAllConfiguration)
            {
                ToggleOnePerFeature(
                    name,
                    targetSuppress: suppress,
                    scope: scope,
                    blindApply: true,
                    res: res);

                return;
            }

            // For explicit std_cut/non_std_cut passes, never use SetSuppression2 here.
            // Apply through active selection so std_cut and non_std_cut keep different states.
            ToggleOneByActiveSelection(name, suppress, res);
        }

        private void ToggleOneByActiveSelection(
            string name,
            bool suppress,
            ToggleResult res)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _model.ClearSelection2(true);

            if (!TrySelectByNameHeuristics(name, append: false))
            {
                res.Missing.Add(name);
                _model.ClearSelection2(true);
                return;
            }

            if (TrySetSelectionSuppression(suppress, out var err))
            {
                if (suppress)
                    res.Suppressed.Add(name);
                else
                    res.Unsuppressed.Add(name);
            }
            else
            {
                res.Failed[name] = err;
            }

            _model.ClearSelection2(true);
        }

        private void VerifyCriticalUnsuppressed(
            string[] names,
            swInConfigurationOpts_e scope,
            ToggleResult res)
        {
            if (names.Length == 0)
                return;

            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_index.TryGetValue(name, out var entry))
                    continue;

                entry.IsSuppressedCached = null;

                if (!TryGetIsSuppressed(entry, scope, out var isSuppressed))
                    continue;

                if (!isSuppressed)
                    continue;

                Logger.Warn($"[FeatureToggleBatch] Critical feature '{name}' is still suppressed after first pass; retrying once.");

                ToggleOneCritical(
                    name,
                    suppress: false,
                    scope,
                    res);
            }
        }

        private static string[] ExtractOrdered(string[] source, string[] preferredOrder)
        {
            if (source.Length == 0)
                return Array.Empty<string>();

            var sourceSet = new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
            return preferredOrder.Where(sourceSet.Contains).ToArray();
        }

        private static string[] RemoveNames(string[] source, string[] namesToRemove)
        {
            if (source.Length == 0 || namesToRemove.Length == 0)
                return source;

            var removeSet = new HashSet<string>(namesToRemove, StringComparer.OrdinalIgnoreCase);
            return source.Where(x => !removeSet.Contains(x)).ToArray();
        }

        private void ApplyBySelectionBatches(
            string[] names,
            bool suppress,
            swInConfigurationOpts_e scope,
            ToggleOptions options,
            ToggleResult res)
        {
            if (names.Length == 0)
                return;

            int batchSize = Math.Max(1, options.BatchSize);

            for (int i = 0; i < names.Length; i += batchSize)
            {
                var batch = names.Skip(i).Take(batchSize).ToArray();

                if (options.ClearSelectionPerBatch)
                    _model.ClearSelection2(true);

                var selected = new List<string>(batch.Length);

                foreach (var name in batch)
                {
                    if (!options.BlindApply && _index.TryGetValue(name, out var entry))
                    {
                        if (TryGetIsSuppressed(entry, scope, out var cur) && cur == suppress)
                        {
                            res.SkippedAlreadyCorrect.Add(name);
                            continue;
                        }
                    }

                    if (TrySelectByNameHeuristics(name, append: true))
                        selected.Add(name);
                    else
                        res.Missing.Add(name);
                }

                if (selected.Count == 0)
                    continue;

                if (!TrySetSelectionSuppression(suppress, out var err))
                {
                    if (options.FallbackToPerFeature)
                    {
                        foreach (var nm in selected)
                            ToggleOnePerFeature(nm, suppress, scope, blindApply: true, res, forceFallbackOnly: true);
                    }
                    else
                    {
                        foreach (var nm in selected)
                            res.Failed[nm] = err;
                    }

                    continue;
                }

                if (suppress)
                    res.Suppressed.AddRange(selected);
                else
                    res.Unsuppressed.AddRange(selected);
            }
        }

        /// <summary>
        /// Calls EditSuppress2 / EditUnsuppress2 on the current selection.
        /// These methods affect only the active configuration.
        /// </summary>
        private bool TrySetSelectionSuppression(bool suppress, out string error)
        {
            error = string.Empty;

            try
            {
                bool ok = suppress
                    ? _model.EditSuppress2()
                    : _model.EditUnsuppress2();

                if (!ok)
                {
                    error = suppress
                        ? "EditSuppress2 returned false."
                        : "EditUnsuppress2 returned false.";

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private bool TrySelectByNameHeuristics(string name, bool append)
        {
            const int mark = 0;
            const double x = 0, y = 0, z = 0;

            try
            {
                if (_ext.SelectByID2(name, "FEATURE", x, y, z, append, mark, null, 0))
                    return true;

                if (_ext.SelectByID2(name, "SKETCH", x, y, z, append, mark, null, 0))
                    return true;

                if (_ext.SelectByID2(name, "BODYFEATURE", x, y, z, append, mark, null, 0))
                    return true;

                if (_ext.SelectByID2(name, "REFERENCECURVES", x, y, z, append, mark, null, 0))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void ToggleOnePerFeature(
            string name,
            bool targetSuppress,
            swInConfigurationOpts_e scope,
            bool blindApply,
            ToggleResult res,
            bool forceFallbackOnly = false)
        {
            if (!_index.TryGetValue(name, out var entry))
            {
                res.Missing.Add(name);
                return;
            }

            if (!forceFallbackOnly && !blindApply)
            {
                if (TryGetIsSuppressed(entry, scope, out var current) && current == targetSuppress)
                {
                    res.SkippedAlreadyCorrect.Add(name);
                    return;
                }
            }

            if (TrySet(entry, targetSuppress, scope, out var err))
            {
                entry.IsSuppressedCached = targetSuppress;

                if (targetSuppress)
                    res.Suppressed.Add(name);
                else
                    res.Unsuppressed.Add(name);
            }
            else
            {
                res.Failed[name] = err;
            }
        }

        /// <summary>
        /// Calls Feature.SetSuppression2. This is required for swAllConfiguration.
        /// </summary>
        private static bool TrySet(
            FeatureEntry entry,
            bool suppress,
            swInConfigurationOpts_e scope,
            out string error)
        {
            error = string.Empty;

            try
            {
                entry.Feature.SetSuppression2(
                    suppress
                        ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                        : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)scope,
                    null);

                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static bool TryGetIsSuppressed(
            FeatureEntry entry,
            swInConfigurationOpts_e scope,
            out bool isSuppressed)
        {
            isSuppressed = false;

            if (entry.IsSuppressedCached.HasValue)
            {
                isSuppressed = entry.IsSuppressedCached.Value;
                return true;
            }

            try
            {
                object? raw = entry.Feature.IsSuppressed2((int)scope, null);

                if (!TryDecodeSuppressionVariant(raw, out var sup))
                    return false;

                isSuppressed = sup;
                entry.IsSuppressedCached = sup;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeSuppressionVariant(object? raw, out bool suppressed)
        {
            suppressed = false;

            if (raw is null)
                return false;

            switch (raw)
            {
                case bool b:
                    suppressed = b;
                    return true;

                case int i:
                    suppressed = i != 0;
                    return true;

                case short s:
                    suppressed = s != 0;
                    return true;

                case long l:
                    suppressed = l != 0;
                    return true;
            }

            if (raw is Array arr && arr.Length > 0)
            {
                var first = arr.GetValue(0);

                if (first is Array nested && nested.Length > 0)
                    first = nested.GetValue(0);

                switch (first)
                {
                    case bool bb:
                        suppressed = bb;
                        return true;

                    case int ii:
                        suppressed = ii != 0;
                        return true;

                    case short ss:
                        suppressed = ss != 0;
                        return true;

                    case long ll:
                        suppressed = ll != 0;
                        return true;

                    case object o when o is bool b2:
                        suppressed = b2;
                        return true;

                    case object o when o is int i2:
                        suppressed = i2 != 0;
                        return true;

                    case object o when o is short s2:
                        suppressed = s2 != 0;
                        return true;

                    case object o when o is long l2:
                        suppressed = l2 != 0;
                        return true;
                }
            }

            return false;
        }

        private static void TryAdd(Dictionary<string, FeatureEntry> map, Feature f)
        {
            try
            {
                var name = f?.Name;

                if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name))
                    map.Add(name, new FeatureEntry(f));
            }
            catch
            {
                // Ignore bad SolidWorks feature state.
            }
        }

        private static IEnumerable<string> Normalize(IEnumerable<string> names)
        {
            return names
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class ToggleResult
        {
            public List<string> Suppressed { get; } = new();

            public List<string> Unsuppressed { get; } = new();

            public List<string> SkippedAlreadyCorrect { get; } = new();

            public List<string> Missing { get; } = new();

            public Dictionary<string, string> Failed { get; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
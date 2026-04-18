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

        private FeatureToggleBatch(ModelDoc2 model, Dictionary<string, FeatureEntry> index)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _ext   = (IModelDocExtension)model.Extension;
            _index = index;
        }

        private sealed class FeatureEntry
        {
            public Feature Feature { get; }
            public bool? IsSuppressedCached { get; set; }
            public FeatureEntry(Feature feature) => Feature = feature;
        }

        public sealed class ToggleOptions
        {
            /// <summary>
            /// If true, skip IsSuppressed2 reads (faster). Applied only on the
            /// selection-batch path (ThisConfiguration). The per-feature path always
            /// calls SetSuppression2 unconditionally — reading state first would add
            /// COM round-trips with no real benefit across all configs.
            /// Default: true.
            /// </summary>
            public bool BlindApply { get; init; } = true;

            /// <summary>
            /// Use selection-batch suppression for ThisConfiguration jobs.
            /// Ignored when scope == swAllConfiguration (per-feature path is always used).
            /// Default: true.
            /// </summary>
            public bool UseSelectionBatch { get; init; } = true;

            /// <summary>How many items to select per batch. Default: 80.</summary>
            public int BatchSize { get; init; } = 80;

            /// <summary>Clear selection before each batch. Default: true.</summary>
            public bool ClearSelectionPerBatch { get; init; } = true;

            /// <summary>
            /// When selection-batch fails for a name, fallback to per-feature
            /// SetSuppression2 for that name (if it exists in the index).
            /// Default: true.
            /// </summary>
            public bool FallbackToPerFeature { get; init; } = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Build
        // ─────────────────────────────────────────────────────────────────────

        public static FeatureToggleBatch Build(ModelDoc2 model)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
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

        // ─────────────────────────────────────────────────────────────────────
        // Apply
        // ─────────────────────────────────────────────────────────────────────

        public ToggleResult Apply(
            IEnumerable<string>? suppressNames,
            IEnumerable<string>? unsuppressNames,
            swInConfigurationOpts_e scope = swInConfigurationOpts_e.swThisConfiguration,
            ToggleOptions? options = null)
        {
            options ??= new ToggleOptions();

            var res = new ToggleResult();

            // Reset cache — config switch or scope change makes cached state stale.
            foreach (var e in _index.Values)
                e.IsSuppressedCached = null;

            var unsup = unsuppressNames is null ? Array.Empty<string>() : Normalize(unsuppressNames).ToArray();
            var sup   = suppressNames   is null ? Array.Empty<string>() : Normalize(suppressNames).ToArray();

            // Unsuppress wins when a name appears in both lists.
            if (unsup.Length > 0 && sup.Length > 0)
                sup = sup.Where(s => !unsup.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

            Logger.Info(
                $"[FeatureToggleBatch] Apply(scope={scope}) " +
                $"unsup={unsup.Length}, sup={sup.Length}, " +
                $"batch={options.UseSelectionBatch}, blind={options.BlindApply}");

            // ── Path selection ────────────────────────────────────────────────
            //
            // swAllConfiguration MUST use the per-feature path.
            // EditSuppress2 / EditUnsuppress2 (selection-batch) have no scope
            // parameter — they always operate on the active configuration only,
            // regardless of what is selected or what scope you want.
            // Feature.SetSuppression2 is the only SW API that accepts a scope.
            //
            if (scope == swInConfigurationOpts_e.swAllConfiguration)
            {
                Logger.Info("[FeatureToggleBatch] scope=AllConfiguration → per-feature path (SetSuppression2).");

                // Unsuppress first (safer order).
                foreach (var name in unsup)
                    ToggleOnePerFeature(name, targetSuppress: false, scope, blindApply: true, res);

                foreach (var name in sup)
                    ToggleOnePerFeature(name, targetSuppress: true, scope, blindApply: true, res);
            }
            else if (options.UseSelectionBatch)
            {
                // Unsuppress first.
                ApplyBySelectionBatches(unsup, suppress: false, scope, options, res);
                ApplyBySelectionBatches(sup,   suppress: true,  scope, options, res);
            }
            else
            {
                foreach (var name in unsup)
                    ToggleOnePerFeature(name, targetSuppress: false, scope, options.BlindApply, res);

                foreach (var name in sup)
                    ToggleOnePerFeature(name, targetSuppress: true,  scope, options.BlindApply, res);
            }

            Logger.Info(
                "[FeatureToggleBatch] Apply done → " +
                $"unsuppressed={res.Unsuppressed.Count}, suppressed={res.Suppressed.Count}, " +
                $"skipped={res.SkippedAlreadyCorrect.Count}, missing={res.Missing.Count}, failed={res.Failed.Count}");

            if (res.Missing.Count > 0)
                Logger.Warn("[FeatureToggleBatch] Missing: " + string.Join(", ", res.Missing.Take(50)));

            if (res.Failed.Count > 0)
                Logger.Warn("[FeatureToggleBatch] Failed: " +
                    string.Join(", ", res.Failed.Take(20).Select(kv => $"{kv.Key} => {kv.Value}")));

            return res;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Single feature toggle (public convenience)
        // ─────────────────────────────────────────────────────────────────────

        public bool TryToggle(
            string featureName,
            bool suppress,
            swInConfigurationOpts_e scope = swInConfigurationOpts_e.swThisConfiguration)
        {
            if (string.IsNullOrWhiteSpace(featureName)) return false;

            var name = featureName.Trim();
            if (!_index.TryGetValue(name, out var entry)) return false;

            if (TryGetIsSuppressed(entry, scope, out var isSuppressed) && isSuppressed == suppress)
                return true; // already correct

            if (!TrySet(entry, suppress, scope, out _)) return false;

            entry.IsSuppressedCached = suppress;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Selection-batch path (ThisConfiguration only)
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyBySelectionBatches(
            string[] names,
            bool suppress,
            swInConfigurationOpts_e scope,
            ToggleOptions options,
            ToggleResult res)
        {
            if (names.Length == 0) return;

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

                if (selected.Count == 0) continue;

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

                if (suppress) res.Suppressed.AddRange(selected);
                else          res.Unsuppressed.AddRange(selected);
            }
        }

        /// <summary>
        /// Calls EditSuppress2 / EditUnsuppress2 on the current selection.
        /// Note: these methods have no scope parameter — they always affect the
        /// active configuration only. This is intentional: this method is only
        /// called from the ThisConfiguration path.
        /// </summary>
        private bool TrySetSelectionSuppression(bool suppress, out string error)
        {
            error = string.Empty;
            try
            {
                bool ok = suppress ? _model.EditSuppress2() : _model.EditUnsuppress2();
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
                if (_ext.SelectByID2(name, "FEATURE",      x, y, z, append, mark, null, 0)) return true;
                if (_ext.SelectByID2(name, "SKETCH",       x, y, z, append, mark, null, 0)) return true;
                if (_ext.SelectByID2(name, "BODYFEATURE",  x, y, z, append, mark, null, 0)) return true;
                if (_ext.SelectByID2(name, "REFERENCECURVES", x, y, z, append, mark, null, 0)) return true;
                return false;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Per-feature path (required for AllConfiguration; fallback for batch)
        // ─────────────────────────────────────────────────────────────────────

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

            // Skip state-read when blind (AllConfiguration path is always blind —
            // IsSuppressed2 only reports the active config anyway, not all configs).
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
                if (targetSuppress) res.Suppressed.Add(name);
                else                res.Unsuppressed.Add(name);
            }
            else
            {
                res.Failed[name] = err;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Low-level SW helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Calls Feature.SetSuppression2. This is the only SW API that accepts a
        /// scope parameter and actually writes state across all configurations
        /// when scope == swAllConfiguration.
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

        /// <summary>
        /// Reads suppression state from IsSuppressed2.
        /// NOTE: IsSuppressed2 reports the state for the ACTIVE configuration only,
        /// regardless of the scope argument. Do not use this to verify AllConfiguration
        /// results — the cached value will only reflect the active config.
        /// Returns false if the result cannot be decoded.
        /// </summary>
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
            catch { return false; }
        }

        /// <summary>
        /// Decodes the VARIANT result from IsSuppressed2.
        /// SW may return bool, int, short, long, or arrays of any of those.
        /// </summary>
        private static bool TryDecodeSuppressionVariant(object? raw, out bool suppressed)
        {
            suppressed = false;
            if (raw is null) return false;

            switch (raw)
            {
                case bool b:  suppressed = b;      return true;
                case int i:   suppressed = i != 0; return true;
                case short s: suppressed = s != 0; return true;
                case long l:  suppressed = l != 0; return true;
            }

            if (raw is Array arr && arr.Length > 0)
            {
                var first = arr.GetValue(0);

                // Handle nested arrays (some SW versions return bool[][] )
                if (first is Array nested && nested.Length > 0)
                    first = nested.GetValue(0);

                switch (first)
                {
                    case bool bb:  suppressed = bb;       return true;
                    case int ii:   suppressed = ii != 0;  return true;
                    case short ss: suppressed = ss != 0;  return true;
                    case long ll:  suppressed = ll != 0;  return true;
                    case object o when o is bool b2:  suppressed = b2;       return true;
                    case object o when o is int i2:   suppressed = i2 != 0;  return true;
                    case object o when o is short s2: suppressed = s2 != 0;  return true;
                    case object o when o is long l2:  suppressed = l2 != 0;  return true;
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
            catch { /* ignore — feature may be in a bad state */ }
        }

        private static IEnumerable<string> Normalize(IEnumerable<string> names)
            => names
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

        // ─────────────────────────────────────────────────────────────────────
        // Result
        // ─────────────────────────────────────────────────────────────────────

        public sealed class ToggleResult
        {
            public List<string> Suppressed            { get; } = new();
            public List<string> Unsuppressed          { get; } = new();
            /// <summary>Items found but already in the correct state (active config read only).</summary>
            public List<string> SkippedAlreadyCorrect { get; } = new();
            /// <summary>Name not found in the feature index / not selectable.</summary>
            public List<string> Missing               { get; } = new();
            /// <summary>name → error message</summary>
            public Dictionary<string, string> Failed  { get; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
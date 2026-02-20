// ModelAutomation/SolidWorks/FeatureToggleBatch.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application; // Logger

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    /// <summary>
    /// Fast, macro-style feature suppression/unsuppression:
    /// - Builds a feature index once (top-level + sub-features)
    /// - Caches suppression state per feature (per configuration scope)
    /// - Skips COM calls when state is already correct
    /// - Does NOT rebuild (caller controls single rebuild at end)
    ///
    /// IMPORTANT:
    /// Feature.IsSuppressed2 can return VARIANT arrays (bool[], int[], object[]).
    /// We decode those properly; otherwise we may mis-detect suppression state.
    /// </summary>
    public sealed class FeatureToggleBatch
    {
        private readonly Dictionary<string, FeatureEntry> _index;

        private FeatureToggleBatch(Dictionary<string, FeatureEntry> index)
        {
            _index = index;
        }

        private sealed class FeatureEntry
        {
            public Feature Feature { get; }
            public bool? IsSuppressedCached { get; set; } // cache for current config scope

            public FeatureEntry(Feature feature)
            {
                Feature = feature;
            }
        }

        /// <summary>
        /// Build an index of all features and sub-features by name (case-insensitive).
        /// Call once per opened model (per job/config).
        /// </summary>
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
            return new FeatureToggleBatch(map);
        }

        /// <summary>
        /// Batch apply:
        /// - suppressNames: features to suppress
        /// - unsuppressNames: features to unsuppress
        ///
        /// No rebuild is performed here.
        /// </summary>
        public ToggleResult Apply(
            IEnumerable<string>? suppressNames,
            IEnumerable<string>? unsuppressNames,
            swInConfigurationOpts_e scope = swInConfigurationOpts_e.swThisConfiguration)
        {
            var res = new ToggleResult();

            // Reset cache (important when switching scope or after big changes)
            foreach (var e in _index.Values)
                e.IsSuppressedCached = null;

            // Normalize once (avoid duplicates + whitespace)
            var unsup = unsuppressNames is null ? Array.Empty<string>() : Normalize(unsuppressNames).ToArray();
            var sup = suppressNames is null ? Array.Empty<string>() : Normalize(suppressNames).ToArray();

            // If name appears in both → unsuppress wins
            if (unsup.Length > 0 && sup.Length > 0)
                sup = sup.Where(s => !unsup.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

            Logger.Info($"[FeatureToggleBatch] Apply(scope={scope}) unsup={unsup.Length}, sup={sup.Length}");

            // Unsuppress first (safer)
            foreach (var name in unsup)
                ToggleOne(name, targetSuppress: false, scope, res);

            foreach (var name in sup)
                ToggleOne(name, targetSuppress: true, scope, res);

            Logger.Info(
                "[FeatureToggleBatch] Apply done → " +
                $"unsuppressed={res.Unsuppressed.Count}, suppressed={res.Suppressed.Count}, " +
                $"skipped={res.SkippedAlreadyCorrect.Count}, missing={res.Missing.Count}, failed={res.Failed.Count}");

            if (res.Missing.Count > 0)
                Logger.Warn("[FeatureToggleBatch] Missing: " + string.Join(", ", res.Missing.Take(50)));

            if (res.Failed.Count > 0)
                Logger.Warn("[FeatureToggleBatch] Failed: " + string.Join(", ", res.Failed.Take(20).Select(kv => $"{kv.Key} => {kv.Value}")));

            return res;
        }

        /// <summary>
        /// Convenience helper for a single feature name.
        /// Returns false if not found or failed; true if toggled or already-correct.
        /// </summary>
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

            // Skip if already correct
            if (TryGetIsSuppressed(entry, scope, out var isSuppressed))
            {
                if (isSuppressed == suppress)
                    return true; // already correct
            }

            if (!TrySet(entry, suppress, scope, out _))
                return false;

            // cache update on success
            entry.IsSuppressedCached = suppress;
            return true;
        }

        private void ToggleOne(
            string name,
            bool targetSuppress,
            swInConfigurationOpts_e scope,
            ToggleResult res)
        {
            if (!_index.TryGetValue(name, out var entry))
            {
                res.Missing.Add(name);
                return;
            }

            // Read cached or query once
            if (TryGetIsSuppressed(entry, scope, out var current))
            {
                if (current == targetSuppress)
                {
                    res.SkippedAlreadyCorrect.Add(name);
                    return;
                }
            }

            if (TrySet(entry, targetSuppress, scope, out var err))
            {
                entry.IsSuppressedCached = targetSuppress;

                if (targetSuppress) res.Suppressed.Add(name);
                else res.Unsuppressed.Add(name);
            }
            else
            {
                res.Failed[name] = err;
            }
        }

        private static void TryAdd(Dictionary<string, FeatureEntry> map, Feature f)
        {
            try
            {
                var name = f?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    return;

                // Keep first occurrence by name (predictable)
                if (!map.ContainsKey(name))
                    map.Add(name, new FeatureEntry(f));
            }
            catch
            {
                // ignore
            }
        }

        private static IEnumerable<string> Normalize(IEnumerable<string> names)
            => names
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads suppression state. IMPORTANT: IsSuppressed2 may return arrays.
        /// </summary>
        private static bool TryGetIsSuppressed(
            FeatureEntry entry,
            swInConfigurationOpts_e scope,
            out bool isSuppressed)
        {
            isSuppressed = false;

            // Cached?
            if (entry.IsSuppressedCached.HasValue)
            {
                isSuppressed = entry.IsSuppressedCached.Value;
                return true;
            }

            try
            {
                object? raw = entry.Feature.IsSuppressed2((int)scope, null);

                if (!TryDecodeSuppressionVariant(raw, out var sup))
                    return false; // unknown -> don't skip toggles incorrectly

                isSuppressed = sup;
                entry.IsSuppressedCached = sup;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decodes VARIANT results from IsSuppressed2.
        /// Handles:
        /// - bool / int / short / long
        /// - bool[] / int[] / object[]
        /// For arrays, we take the FIRST element (works for single-config "Default").
        /// </summary>
        private static bool TryDecodeSuppressionVariant(object? raw, out bool suppressed)
        {
            suppressed = false;

            if (raw is null)
                return false;

            switch (raw)
            {
                case bool b: suppressed = b; return true;
                case int i: suppressed = i != 0; return true;
                case short s: suppressed = s != 0; return true;
                case long l: suppressed = l != 0; return true;
            }

            if (raw is Array arr && arr.Length > 0)
            {
                var first = arr.GetValue(0);

                if (first is Array nested && nested.Length > 0)
                    first = nested.GetValue(0);

                switch (first)
                {
                    case bool bb: suppressed = bb; return true;
                    case int ii: suppressed = ii != 0; return true;
                    case short ss: suppressed = ss != 0; return true;
                    case long ll: suppressed = ll != 0; return true;
                }

                if (first is object o)
                {
                    if (o is bool b2) { suppressed = b2; return true; }
                    if (o is int i2) { suppressed = i2 != 0; return true; }
                    if (o is short s2) { suppressed = s2 != 0; return true; }
                    if (o is long l2) { suppressed = l2 != 0; return true; }
                }

                return false;
            }

            return false;
        }

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

        public sealed class ToggleResult
        {
            public List<string> Suppressed { get; } = new();
            public List<string> Unsuppressed { get; } = new();

            /// <summary>Features found, but already in correct state (COM call skipped).</summary>
            public List<string> SkippedAlreadyCorrect { get; } = new();

            /// <summary>Feature name not found in index.</summary>
            public List<string> Missing { get; } = new();

            /// <summary>name -> error</summary>
            public Dictionary<string, string> Failed { get; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}

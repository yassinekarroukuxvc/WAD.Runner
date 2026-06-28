using System;
using System.Collections;
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
            "non_std_cut_plan_feature",
            "ERW_STD_feature",
            "ERW_STD_sketch",
            "ERW_180_DEG_REV_feature",
            "ERW_180_DEG_REV_sketch"
        };

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

            public string? SelectionType { get; set; }

            public bool? IsSuppressedCached { get; set; }

            public FeatureEntry(Feature feature) => Feature = feature;
        }

        public sealed class ToggleOptions
        {
            public bool BlindApply { get; init; } = true;

            public bool UseSelectionBatch { get; init; } = true;

            public int BatchSize { get; init; } = 250;

            public bool ClearSelectionPerBatch { get; init; } = true;

            public bool FallbackToPerFeature { get; init; } = true;

            public bool InputIsNormalized { get; init; } = false;
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
                : NormalizeInput(unsuppressNames, options.InputIsNormalized).ToArray();

            var sup = suppressNames is null
                ? Array.Empty<string>()
                : NormalizeInput(suppressNames, options.InputIsNormalized).ToArray();

            if (unsup.Length > 0 && sup.Length > 0)
                sup = sup.Where(s => !unsup.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

            var criticalUnsup = ExtractOrdered(unsup, CriticalUnsuppressOrder);
            var criticalSup = ExtractOrdered(sup, CriticalSuppressOrder);

            if (criticalUnsup.Length > 0)
                unsup = RemoveNames(unsup, criticalUnsup);

            if (criticalSup.Length > 0)
                sup = RemoveNames(sup, criticalSup);

            Logger.Info(
                $"[FeatureToggleBatch] Apply(scope={scope}) " +
                $"unsup={unsup.Length}+critical({criticalUnsup.Length}), " +
                $"sup={sup.Length}+critical({criticalSup.Length}), " +
                $"batch={options.UseSelectionBatch}, blind={options.BlindApply}");

            if (scope == swInConfigurationOpts_e.swAllConfiguration)
            {
                Logger.Info("[FeatureToggleBatch] scope=AllConfiguration → per-feature path (SetSuppression2).");

                if (!options.BlindApply)
                    PreReadSuppressionCache(unsup.Concat(sup).Concat(criticalUnsup).Concat(criticalSup), scope);

                ApplyCriticalInOrder(criticalUnsup, suppress: false, scope, res);

                foreach (var name in unsup)
                    ToggleOnePerFeature(name, targetSuppress: false, scope, blindApply: options.BlindApply, res);

                foreach (var name in sup)
                    ToggleOnePerFeature(name, targetSuppress: true, scope, blindApply: options.BlindApply, res);

                ApplyCriticalInOrder(criticalSup, suppress: true, scope, res);
            }
            else if (options.UseSelectionBatch)
            {
                ApplyCriticalInOrder(criticalUnsup, suppress: false, scope, res);

                ApplyBySelectionBatches(unsup, suppress: false, scope, options, res);
                ApplyBySelectionBatches(sup, suppress: true, scope, options, res);

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
            VerifyCriticalSuppressed(criticalSup, scope, res);

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
            if (scope == swInConfigurationOpts_e.swAllConfiguration)
            {
                ToggleOnePerFeature(name, targetSuppress: suppress, scope, blindApply: true, res);
                return;
            }

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
                if (suppress) res.Suppressed.Add(name);
                else res.Unsuppressed.Add(name);
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

                Logger.Warn($"[FeatureToggleBatch] Critical feature '{name}' still suppressed after first pass; retrying.");

                entry.IsSuppressedCached = null;
                ToggleOneCritical(name, suppress: false, scope, res);

                entry.IsSuppressedCached = null;

                if (TryGetIsSuppressed(entry, scope, out var stillSuppressed) && stillSuppressed)
                {
                    Logger.Error($"[FeatureToggleBatch] Critical feature '{name}' could not be unsuppressed after retry.");
                    res.Failed.TryAdd(name, "Still suppressed after retry.");
                }
            }
        }

        private void VerifyCriticalSuppressed(
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

                if (isSuppressed)
                    continue;

                Logger.Warn(
                    $"[FeatureToggleBatch] Critical feature '{name}' still unsuppressed after first pass " +
                    $"(likely driven by a parent feature); retrying suppress.");

                entry.IsSuppressedCached = null;
                ToggleOneCritical(name, suppress: true, scope, res);

                entry.IsSuppressedCached = null;

                if (TryGetIsSuppressed(entry, scope, out var stillUnsuppressed) && !stillUnsuppressed)
                    continue;

                Logger.Error(
                    $"[FeatureToggleBatch] Critical feature '{name}' could not be suppressed after retry. " +
                    $"Check whether its driving parent feature is still unsuppressed.");
                res.Failed.TryAdd(name, "Still unsuppressed after retry (parent-driven?).");
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
            var span = names.AsSpan();

            for (int i = 0; i < span.Length; i += batchSize)
            {
                var batch = span.Slice(i, Math.Min(batchSize, span.Length - i));

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

                if (suppress) res.Suppressed.AddRange(selected);
                else res.Unsuppressed.AddRange(selected);
            }
        }


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
                if (_index.TryGetValue(name, out var entry) && entry.SelectionType is { } knownType)
                {
                    if (_ext.SelectByID2(name, knownType, x, y, z, append, mark, null, 0))
                        return true;

                }

                foreach (var type in SelectionProbeOrder)
                {
                    if (_ext.SelectByID2(name, type, x, y, z, append, mark, null, 0))
                    {
                        if (_index.TryGetValue(name, out var e))
                            e.SelectionType = type;

                        return true;
                    }
                }

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

                if (targetSuppress) res.Suppressed.Add(name);
                else res.Unsuppressed.Add(name);
            }
            else
            {
                res.Failed[name] = err;
            }
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
                bool ok = entry.Feature.SetSuppression2(
                    suppress
                        ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                        : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)scope,
                    null);

                if (!ok)
                {
                    error = suppress
                        ? "SetSuppression2 returned false (suppress)."
                        : "SetSuppression2 returned false (unsuppress).";

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
                }
            }

            return false;
        }


        private static void TryAdd(Dictionary<string, FeatureEntry> map, Feature f)
        {
            try
            {
                var name = f?.Name;

                if (string.IsNullOrWhiteSpace(name) || map.ContainsKey(name))
                    return;

                var entry = new FeatureEntry(f);

                var swType = f.GetTypeName2() as string ?? string.Empty;
                entry.SelectionType = SwTypeToSelectionType.TryGetValue(swType, out var selType)
                    ? selType
                    : "FEATURE";

                map.Add(name, entry);
            }
            catch
            {
            }
        }

        private void PreReadSuppressionCache(IEnumerable<string> names, swInConfigurationOpts_e scope)
        {
            foreach (var name in names)
            {
                if (_index.TryGetValue(name, out var entry) && entry.IsSuppressedCached is null)
                    TryGetIsSuppressed(entry, scope, out _);
            }
        }


        private static IEnumerable<string> NormalizeInput(IEnumerable<string> names, bool alreadyNormalized)
        {
            return alreadyNormalized ? NormalizeFast(names) : Normalize(names);
        }

        private static IEnumerable<string> NormalizeFast(IEnumerable<string> names)
        {
            return names
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim());
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

            public bool IsSuccess => Missing.Count == 0 && Failed.Count == 0;
        }
    }
}

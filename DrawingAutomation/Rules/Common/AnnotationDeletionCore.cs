// DrawingAutomation/Rules/Common/AnnotationDeletionCore.cs
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Shared core for "delete-by-fullname" annotation planning:
/// - SolidWorks scanning: existing DisplayDimension Dimension.FullName per view
/// - Planning: EXISTING - KEEP (smart matched)
/// - Candidate filtering: candidates ∩ existing (smart matched)
/// - Name normalization + candidate variants
/// - Dumps/diagnostics
///
/// Wedge-specific rules files should only define:
/// - their enums/options
/// - their KnownSuperset (all possible annotations they know about)
/// - their Keep rules (subset to keep)
/// </summary>
public static class AnnotationDeletionCore
{
    // ---------------------------
    // Common types
    // ---------------------------

    public enum ViewKind
    {
        Front,
        Side,
        Top,
        Detail,
        Section
    }

    public sealed record DeletionTarget(string ViewName, string AnnotationFullName);

    public sealed record Ann(ViewKind View, string FullName);

    /// <summary>
    /// Nominal view-name map (what you pass in).
    /// </summary>
    public sealed class ViewNameMap
    {
        public string Front { get; init; } = "Front View";
        public string Side { get; init; } = "Side View";
        public string Top { get; init; } = "Top View";
        public string Detail { get; init; } = "Detail View";
        public string Section { get; init; } = "Section View";

        public string Resolve(ViewKind kind) => kind switch
        {
            ViewKind.Front => Front,
            ViewKind.Side => Side,
            ViewKind.Top => Top,
            ViewKind.Detail => Detail,
            ViewKind.Section => Section,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        public IEnumerable<string> AllNominalNames()
            => new[] { Front, Side, Top, Detail, Section };
    }

    // ---------------------------
    // Dumps / diagnostics
    // ---------------------------

    public static void DumpDeletionPlan(
        string title,
        IReadOnlyList<DeletionTarget> deletions,
        string tagPrefix,
        int maxPerView = 200)
    {
        if (deletions == null)
        {
            Logger.Warn($"[{tagPrefix}.PlanDump] {title}: deletions list is NULL.");
            return;
        }

        Logger.Info($"[{tagPrefix}.PlanDump] {title}: total deletions planned = {deletions.Count}");

        if (deletions.Count == 0)
            return;

        var byView = deletions
            .GroupBy(d => d.ViewName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var g in byView)
        {
            Logger.Info($"[{tagPrefix}.PlanDump] View '{g.Key}' planned deletions = {g.Count()}");

            int i = 0;
            foreach (var d in g)
            {
                if (i++ >= maxPerView)
                {
                    Logger.Info($"[{tagPrefix}.PlanDump]   ... truncated (maxPerView={maxPerView})");
                    break;
                }

                Logger.Info($"[{tagPrefix}.PlanDump]   - {d.AnnotationFullName}");
            }
        }
    }

    public static void DumpExistingDisplayDimensionFullNamesFromDrawing(
        ModelDoc2 drawingModel,
        ViewNameMap? viewNames,
        string tagPrefix,
        bool activateEachView = true,
        int maxPerView = 250)
    {
        if (drawingModel == null)
        {
            Logger.Warn($"[{tagPrefix}.ExistingDump] drawingModel is null.");
            return;
        }

        if (drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
        {
            Logger.Warn($"[{tagPrefix}.ExistingDump] ModelDoc2 is not a drawing.");
            return;
        }

        viewNames ??= new ViewNameMap();

        var existing = CollectExistingDisplayDimensionFullNamesByView(drawingModel, viewNames, activateEachView);

        foreach (var kv in existing)
        {
            var viewName = kv.Key;
            var list = kv.Value?.ToList() ?? new List<string>();

            Logger.Info($"[{tagPrefix}.ExistingDump] View '{viewName}' existing DIM full-names = {list.Count}");

            int i = 0;
            foreach (var s in list)
            {
                if (i++ >= maxPerView)
                {
                    Logger.Info($"[{tagPrefix}.ExistingDump]   ... truncated (maxPerView={maxPerView})");
                    break;
                }

                Logger.Info($"[{tagPrefix}.ExistingDump]   - {s}");
            }
        }
    }

    // ---------------------------
    // CAD-agnostic helpers
    // ---------------------------

    public static IReadOnlyList<DeletionTarget> GetAnnotationsToDelete_FromKnownSuperset(
        HashSet<Ann> keep,
        HashSet<Ann> allKnown,
        ViewNameMap viewNames)
    {
        var deletions = allKnown
            .Where(a => !keep.Contains(a))
            .Select(a => new DeletionTarget(viewNames.Resolve(a.View), a.FullName))
            .ToList();

        return new ReadOnlyCollection<DeletionTarget>(deletions);
    }

    public static IReadOnlyList<DeletionTarget> FilterCandidatesByExisting_FromKnownSuperset(
        IReadOnlyList<DeletionTarget> candidates,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByViewName)
    {
        var results = new List<DeletionTarget>();

        foreach (var viewGroup in candidates.GroupBy(c => c.ViewName, StringComparer.OrdinalIgnoreCase))
        {
            var viewName = viewGroup.Key;

            if (!existingByViewName.TryGetValue(viewName, out var existing) || existing == null || existing.Count == 0)
                continue;

            var matcher = new SmartAnnotationMatcher(existing);

            foreach (var cand in viewGroup)
            {
                if (matcher.TryFindExistingMatch(cand.AnnotationFullName, out var actual))
                    results.Add(new DeletionTarget(viewName, actual));
            }
        }

        var distinct = results
            .DistinctBy(x => NormalizeName(x.ViewName) + "||" + NormalizeName(x.AnnotationFullName))
            .ToList();

        return new ReadOnlyCollection<DeletionTarget>(distinct);
    }

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> BuildKeepExpectedFullNamesByView(
        HashSet<Ann> keep,
        ViewNameMap viewNames)
    {
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ann in keep)
        {
            if (string.IsNullOrWhiteSpace(ann.FullName))
                continue;

            var viewName = viewNames.Resolve(ann.View);

            if (!dict.TryGetValue(viewName, out var list))
            {
                list = new List<string>();
                dict[viewName] = list;
            }

            list.Add(ann.FullName.Trim());
        }

        return dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyCollection<string>)kv.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<DeletionTarget> GetExistingMinusKeep(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByView,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> keepExpectedByView)
    {
        var results = new List<DeletionTarget>();

        foreach (var kv in existingByView)
        {
            var viewName = kv.Key;

            var existing = kv.Value?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            if (existing.Count == 0)
                continue;

            keepExpectedByView.TryGetValue(viewName, out var keepExpectedRaw);

            var keepExpected = keepExpectedRaw?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            var matcher = new SmartAnnotationMatcher(existing);

            // expected keep -> actual existing keep
            var keepActual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var exp in keepExpected)
            {
                if (matcher.TryFindExistingMatch(exp, out var actual))
                    keepActual.Add(actual);
            }

            // delete = existing - keepActual
            foreach (var e in existing)
            {
                if (!keepActual.Contains(e))
                    results.Add(new DeletionTarget(viewName, e));
            }
        }

        return new ReadOnlyCollection<DeletionTarget>(
            results
                .DistinctBy(x => NormalizeName(x.ViewName) + "||" + NormalizeName(x.AnnotationFullName))
                .ToList());
    }

    // ---------------------------
    // SolidWorks scanning (existing names)
    // ---------------------------

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> CollectExistingDisplayDimensionFullNamesByView(
        ModelDoc2 drawingModel,
        ViewNameMap viewNames,
        bool activateEachView)
    {
        var byView = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var viewName in viewNames.AllNominalNames())
        {
            var view = TryGetDrawingViewByName(drawingModel, viewName);
            if (view == null)
            {
                byView[viewName] = Array.Empty<string>();
                continue;
            }

            if (activateEachView)
            {
                try
                {
                    drawingModel.Extension?.SelectByID2(viewName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                }
                catch
                {
                    // ignore
                }
            }

            var names = EnumerateDisplayDimensionFullNames(view)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            byView[viewName] = names;
        }

        return byView;
    }

    private static View? TryGetDrawingViewByName(ModelDoc2 drawingModel, string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return null;

        if (drawingModel is not DrawingDoc drw)
            return null;

        try
        {
            var v = drw.GetFirstView() as View;
            if (v == null) return null;

            // first is sheet, next is first drawing view
            v = v.GetNextView() as View;

            while (v != null)
            {
                var name = SafeGetViewName(v);
                if (string.Equals(name, viewName, StringComparison.OrdinalIgnoreCase))
                    return v;

                v = v.GetNextView() as View;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string SafeGetViewName(View view)
    {
        try { return view?.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static IEnumerable<string> EnumerateDisplayDimensionFullNames(View view)
    {
        if (view == null) yield break;

        object obj;
        try
        {
            obj = view.GetDisplayDimensions();
        }
        catch
        {
            yield break;
        }

        if (obj is not object[] arr || arr.Length == 0)
            yield break;

        foreach (var o in arr)
        {
            if (o is not DisplayDimension dd) continue;

            Dimension? dim = null;
            try { dim = dd.GetDimension() as Dimension; }
            catch { /* ignore */ }

            if (dim == null) continue;

            string full = string.Empty;
            try { full = dim.FullName ?? string.Empty; }
            catch { /* ignore */ }

            if (!string.IsNullOrWhiteSpace(full))
                yield return full.Trim();
        }
    }

    // ---------------------------
    // Smart matcher (expected -> actual existing full name)
    // ---------------------------

    private sealed class SmartAnnotationMatcher
    {
        private readonly Dictionary<string, string> _normalizedToActual;

        public SmartAnnotationMatcher(IEnumerable<string> existing)
        {
            _normalizedToActual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in existing.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var actual = e.Trim();
                var norm = NormalizeName(actual);
                _normalizedToActual.TryAdd(norm, actual);
            }
        }

        public bool TryFindExistingMatch(string expected, out string actual)
        {
            actual = string.Empty;
            if (string.IsNullOrWhiteSpace(expected))
                return false;

            var n = NormalizeName(expected);
            if (_normalizedToActual.TryGetValue(n, out actual))
                return true;

            foreach (var variant in GenerateCandidateNames(expected))
            {
                var vn = NormalizeName(variant);
                if (_normalizedToActual.TryGetValue(vn, out actual))
                    return true;
            }

            return false;
        }
    }

    private static IEnumerable<string> GenerateCandidateNames(string expectedFullName)
    {
        yield return expectedFullName;

        if (expectedFullName.EndsWith("_sketch", StringComparison.OrdinalIgnoreCase))
            yield return expectedFullName[..^"_sketch".Length];

        var at = expectedFullName.IndexOf('@');
        if (at > 0 && at < expectedFullName.Length - 1)
        {
            var key = expectedFullName[..at];
            var sketch = expectedFullName[(at + 1)..];

            if (sketch.EndsWith("_sketch", StringComparison.OrdinalIgnoreCase))
                yield return key + "@" + sketch[..^"_sketch".Length];

            if (sketch.Contains("_FRONT_FRONT_", StringComparison.OrdinalIgnoreCase))
                yield return key + "@" + sketch.Replace("_FRONT_FRONT_", "_FRONT_", StringComparison.OrdinalIgnoreCase);
        }

        if (expectedFullName.Contains("_FRONT_FRONT_", StringComparison.OrdinalIgnoreCase))
            yield return expectedFullName.Replace("_FRONT_FRONT_", "_FRONT_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var trimmed = s.Trim();
        var noSpaces = new string(trimmed.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // reduce to Key@Sketch if present (consistent with your existing behavior)
        var parts = noSpaces.Split('@');
        if (parts.Length >= 2)
            noSpaces = parts[0] + "@" + parts[1];

        return noSpaces.ToUpperInvariant();
    }
}

// ============================================================
// Small LINQ compat helper (DistinctBy) for older TFMs
// ============================================================
internal static class LinqCompat
{
    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector)
        where TKey : notnull
    {
        var seen = new HashSet<TKey>();
        foreach (var item in source)
        {
            if (seen.Add(keySelector(item)))
                yield return item;
        }
    }
}
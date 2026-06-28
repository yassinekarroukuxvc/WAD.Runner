using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

public sealed class AnnotationDiffService
{
    public IReadOnlyList<AnnotationDeletionTarget> GetExistingMinusKeep(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByView,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> keepExpectedByView)
    {
        var results = new List<AnnotationDeletionTarget>();

        foreach (var kv in existingByView ?? new Dictionary<string, IReadOnlyCollection<string>>())
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

            IReadOnlyCollection<string>? keepExpectedRaw = null;
            keepExpectedByView?.TryGetValue(viewName, out keepExpectedRaw);

            var keepExpected = keepExpectedRaw?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            var matcher = new SmartAnnotationMatcher(existing);
            var keepActual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var expected in keepExpected)
            {
                if (matcher.TryFindExistingMatch(expected, out var actual))
                    keepActual.Add(actual);
            }

            foreach (var actual in existing)
            {
                if (!keepActual.Contains(actual))
                    results.Add(new AnnotationDeletionTarget(viewName, actual));
            }
        }

        return new ReadOnlyCollection<AnnotationDeletionTarget>(
            results
                .GroupBy(x => NormalizeName(x.ViewName) + "||" + NormalizeName(x.AnnotationFullName), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList());
    }

    public void DumpDeletionPlan(
        string title,
        IReadOnlyList<AnnotationDeletionTarget> deletions,
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

        foreach (var group in byView)
        {
            Logger.Info($"[{tagPrefix}.PlanDump] View '{group.Key}' planned deletions = {group.Count()}");

            var i = 0;
            foreach (var deletion in group)
            {
                if (i++ >= maxPerView)
                {
                    Logger.Info($"[{tagPrefix}.PlanDump]   ... truncated (maxPerView={maxPerView})");
                    break;
                }

                Logger.Info($"[{tagPrefix}.PlanDump]   - {deletion.AnnotationFullName}");
            }
        }
    }

    public void DumpExisting(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByView,
        string tagPrefix,
        int maxPerView = 250)
    {
        foreach (var kv in existingByView ?? new Dictionary<string, IReadOnlyCollection<string>>())
        {
            var list = kv.Value?.ToList() ?? new List<string>();
            Logger.Info($"[{tagPrefix}.ExistingDump] View '{kv.Key}' existing DIM full-names = {list.Count}");

            var i = 0;
            foreach (var fullName in list)
            {
                if (i++ >= maxPerView)
                {
                    Logger.Info($"[{tagPrefix}.ExistingDump]   ... truncated (maxPerView={maxPerView})");
                    break;
                }

                Logger.Info($"[{tagPrefix}.ExistingDump]   - {fullName}");
            }
        }
    }

    private sealed class SmartAnnotationMatcher
    {
        private readonly Dictionary<string, string> _normalizedToActual;

        public SmartAnnotationMatcher(IEnumerable<string> existing)
        {
            _normalizedToActual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var actual in existing.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()))
            {
                _normalizedToActual.TryAdd(NormalizeName(actual), actual);
            }
        }

        public bool TryFindExistingMatch(string expected, out string actual)
        {
            actual = string.Empty;
            if (string.IsNullOrWhiteSpace(expected))
                return false;

            var normalized = NormalizeName(expected);
            if (_normalizedToActual.TryGetValue(normalized, out actual))
                return true;

            foreach (var variant in GenerateCandidateNames(expected))
            {
                if (_normalizedToActual.TryGetValue(NormalizeName(variant), out actual))
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

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var noSpaces = new string(value.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        var parts = noSpaces.Split('@');
        if (parts.Length >= 2)
            noSpaces = parts[0] + "@" + parts[1];

        return noSpaces.ToUpperInvariant();
    }
}

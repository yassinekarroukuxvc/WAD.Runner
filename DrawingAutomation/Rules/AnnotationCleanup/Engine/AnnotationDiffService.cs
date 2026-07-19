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

        var safeExistingByView =
            existingByView ??
            new Dictionary<string, IReadOnlyCollection<string>>();

        foreach (var kv in safeExistingByView)
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

            /*
             * This variable must be declared before TryGetValue.
             *
             * Using:
             *
             * keepExpectedByView?.TryGetValue(
             *     viewName,
             *     out var keepExpectedRaw);
             *
             * can leave keepExpectedRaw unassigned when
             * keepExpectedByView is null.
             */
            IReadOnlyCollection<string>? keepExpectedRaw = null;

            if (keepExpectedByView is not null)
            {
                keepExpectedByView.TryGetValue(
                    viewName,
                    out keepExpectedRaw);
            }

            var keepExpected = keepExpectedRaw?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            var matcher = new SmartAnnotationMatcher(existing);

            var keepActual =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var expected in keepExpected)
            {
                foreach (var actual in matcher.FindExistingMatches(expected))
                {
                    keepActual.Add(actual);
                }
            }

            /*
             * Fail closed when keep rules exist for the view but none
             * match the current drawing template.
             *
             * This protects against deleting every annotation after a
             * template dimension has been renamed.
             */
            if (keepExpected.Count > 0 && keepActual.Count == 0)
            {
                Logger.Warn(
                    $"[AnnotationDiff] View '{viewName}' has " +
                    $"{existing.Count} existing dimensions and " +
                    $"{keepExpected.Count} expected keep names, but zero matches. " +
                    "Cleanup for this view was skipped to prevent destructive deletion.");

                continue;
            }

            foreach (var actual in existing)
            {
                if (!keepActual.Contains(actual))
                {
                    results.Add(
                        new AnnotationDeletionTarget(
                            viewName,
                            actual));
                }
            }
        }

        var distinctResults = results
            .GroupBy(
                x =>
                    x.ViewName.Trim() +
                    "||" +
                    x.AnnotationFullName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return new ReadOnlyCollection<AnnotationDeletionTarget>(
            distinctResults);
    }

    public void DumpDeletionPlan(
        string title,
        IReadOnlyList<AnnotationDeletionTarget> deletions,
        string tagPrefix,
        int maxPerView = 200)
    {
        if (deletions is null)
        {
            Logger.Warn(
                $"[{tagPrefix}.PlanDump] {title}: deletions list is NULL.");

            return;
        }

        Logger.Info(
            $"[{tagPrefix}.PlanDump] {title}: " +
            $"total deletions planned = {deletions.Count}");

        if (deletions.Count == 0)
            return;

        var byView = deletions
            .GroupBy(
                deletion => deletion.ViewName,
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                group => group.Key,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in byView)
        {
            Logger.Info(
                $"[{tagPrefix}.PlanDump] View '{group.Key}' " +
                $"planned deletions = {group.Count()}");

            var count = 0;

            foreach (var deletion in group)
            {
                if (count++ >= maxPerView)
                {
                    Logger.Info(
                        $"[{tagPrefix}.PlanDump]   ... truncated " +
                        $"(maxPerView={maxPerView})");

                    break;
                }

                Logger.Info(
                    $"[{tagPrefix}.PlanDump]   - " +
                    deletion.AnnotationFullName);
            }
        }
    }

    public void DumpExisting(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> existingByView,
        string tagPrefix,
        int maxPerView = 250)
    {
        var safeExistingByView =
            existingByView ??
            new Dictionary<string, IReadOnlyCollection<string>>();

        foreach (var kv in safeExistingByView)
        {
            var list = kv.Value?.ToList() ?? new List<string>();

            Logger.Info(
                $"[{tagPrefix}.ExistingDump] View '{kv.Key}' " +
                $"existing DIM full-names = {list.Count}");

            var count = 0;

            foreach (var fullName in list)
            {
                if (count++ >= maxPerView)
                {
                    Logger.Info(
                        $"[{tagPrefix}.ExistingDump]   ... truncated " +
                        $"(maxPerView={maxPerView})");

                    break;
                }

                Logger.Info(
                    $"[{tagPrefix}.ExistingDump]   - {fullName}");
            }
        }
    }

    private sealed class SmartAnnotationMatcher
    {
        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyList<string>> _identityToActual;

        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyList<string>> _dimensionToActual;

        public SmartAnnotationMatcher(IEnumerable<string> existing)
        {
            var actuals = (existing ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _identityToActual = actuals
                .GroupBy(
                    AnnotationNameIdentity.Normalize,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        (IReadOnlyList<string>)group
                            .ToList()
                            .AsReadOnly(),
                    StringComparer.OrdinalIgnoreCase);

            _dimensionToActual = actuals
                .GroupBy(
                    AnnotationNameIdentity.GetDimensionName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        (IReadOnlyList<string>)group
                            .ToList()
                            .AsReadOnly(),
                    StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<string> FindExistingMatches(
            string expected)
        {
            if (string.IsNullOrWhiteSpace(expected))
                return Array.Empty<string>();

            var matches =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (
                var identity in
                AnnotationNameIdentity.GetSafeCandidateIdentities(expected))
            {
                if (!_identityToActual.TryGetValue(
                        identity,
                        out var actuals))
                {
                    continue;
                }

                foreach (var actual in actuals)
                {
                    matches.Add(actual);
                }
            }

            if (matches.Count > 0)
            {
                return matches
                    .ToList()
                    .AsReadOnly();
            }

            /*
             * Compatibility fallback:
             *
             * Preserve an annotation by its short dimension name only
             * when that short name occurs exactly once in the view.
             *
             * We never guess when multiple annotations share the same
             * dimension name.
             */
            var dimensionName =
                AnnotationNameIdentity.GetDimensionName(expected);

            if (!string.IsNullOrWhiteSpace(dimensionName) &&
                _dimensionToActual.TryGetValue(
                    dimensionName,
                    out var sameName) &&
                sameName.Count == 1)
            {
                return sameName;
            }

            return Array.Empty<string>();
        }
    }
}
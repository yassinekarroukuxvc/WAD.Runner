using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

public sealed class AnnotationCleanupExecutor
{
    private readonly AnnotationCleanupPlanner _planner;
    private readonly DrawingAnnotationStateReader _stateReader;
    private readonly AnnotationDiffService _diffService;
    private readonly ExactAnnotationDeletionService _deletionService;

    public AnnotationCleanupExecutor(
        AnnotationCleanupPlanner planner,
        DrawingAnnotationStateReader stateReader,
        AnnotationDiffService diffService,
        ExactAnnotationDeletionService? deletionService = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
        _diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
        _deletionService = deletionService ?? new ExactAnnotationDeletionService();
    }

    public IReadOnlyList<AnnotationDeletionTarget> PlanDeletions(
        ModelDoc2 drawingModel,
        AnnotationCleanupContext ctx,
        bool activateEachView)
    {
        var existingByView = _stateReader.CollectExistingDisplayDimensionNames(
            drawingModel,
            ctx.ViewNames,
            activateEachView);

        var keepExpectedByView = _planner.BuildExpectedFullNamesByView(ctx);

        return _diffService.GetExistingMinusKeep(existingByView, keepExpectedByView);
    }

    public int Apply(
        DrawingService drawingService,
        IDictionary<string, string> nameMap,
        AnnotationCleanupContext ctx,
        bool activateEachView,
        string logPrefix)
    {
        _ = nameMap; // View names are already resolved into the cleanup context.

        if (drawingService?.Model is not ModelDoc2 model)
        {
            Logger.Warn($"[{logPrefix}.Cleanup] DrawingService.Model is null or not ModelDoc2; skipping cleanup.");
            return 0;
        }

        var existingByView = _stateReader.CollectExistingDisplayDimensionNames(model, ctx.ViewNames, activateEachView);
        var keepExpectedByView = _planner.BuildExpectedFullNamesByView(ctx);
        var deletions = _diffService.GetExistingMinusKeep(existingByView, keepExpectedByView);

        if (deletions.Count == 0)
        {
            Logger.Info($"[{logPrefix}.Plan] Planned deletions = 0; no cleanup is required.");
            return 0;
        }

        _diffService.DumpDeletionPlan($"{logPrefix} Cleanup Runner", deletions, logPrefix);

        var totalDeleted = 0;
        foreach (var group in deletions.GroupBy(d => d.ViewName, StringComparer.OrdinalIgnoreCase))
        {
            var fullNames = group
                .Select(x => x.AnnotationFullName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = _deletionService.DeleteInView(
                model,
                group.Key,
                fullNames,
                logPrefix);

            totalDeleted += result.DeletedCount;

            Logger.Info(
                $"[{logPrefix}.Cleanup] Deleted in view '{group.Key}': " +
                $"{result.DeletedCount}/{fullNames.Count} planned.");

            if (result.DeletedCount > fullNames.Count)
            {
                throw new InvalidOperationException(
                    $"Cleanup deleted more annotations than planned in view '{group.Key}'.");
            }

            if (result.Failed.Count > 0)
            {
                Logger.Warn(
                    $"[{logPrefix}.Cleanup] Exact deletions not completed in view '{group.Key}': " +
                    string.Join(", ", result.Failed));
            }
        }

        // Final safety audit: every active keep-rule annotation that existed
        // before cleanup must still exist after cleanup.
        var afterByView = _stateReader.CollectExistingDisplayDimensionNames(model, ctx.ViewNames, activateEachView);
        VerifyPreservedKeepAnnotations(existingByView, afterByView, keepExpectedByView, logPrefix);

        Logger.Info($"[{logPrefix}.Cleanup] Total deleted annotations: {totalDeleted}");
        return totalDeleted;
    }

    private static void VerifyPreservedKeepAnnotations(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> beforeByView,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> afterByView,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> expectedByView,
        string logPrefix)
    {
        foreach (var expectedEntry in expectedByView)
        {
            beforeByView.TryGetValue(expectedEntry.Key, out var beforeRaw);
            afterByView.TryGetValue(expectedEntry.Key, out var afterRaw);

            var before = beforeRaw ?? Array.Empty<string>();
            var after = afterRaw ?? Array.Empty<string>();

            foreach (var expected in expectedEntry.Value)
            {
                var protectedBefore = FindExpectedMatches(before, expected);
                if (protectedBefore.Count == 0)
                    continue;

                foreach (var protectedActual in protectedBefore)
                {
                    var stillExists = after.Any(actual =>
                        string.Equals(actual, protectedActual, StringComparison.OrdinalIgnoreCase) ||
                        AnnotationNameIdentity.AreEquivalent(actual, protectedActual));

                    if (!stillExists)
                    {
                        throw new InvalidOperationException(
                            $"[{logPrefix}.SafetyAudit] A keep-rule annotation disappeared from " +
                            $"view '{expectedEntry.Key}': expected='{expected}', actual='{protectedActual}'.");
                    }
                }
            }
        }
    }

    private static IReadOnlyCollection<string> FindExpectedMatches(
        IReadOnlyCollection<string> actualNames,
        string expected)
    {
        var candidateIdentities = AnnotationNameIdentity.GetSafeCandidateIdentities(expected);
        var exactMatches = actualNames
            .Where(actual => candidateIdentities.Contains(
                AnnotationNameIdentity.Normalize(actual),
                StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (exactMatches.Count > 0)
            return exactMatches;

        var expectedDimension = AnnotationNameIdentity.GetDimensionName(expected);
        var sameDimension = actualNames
            .Where(actual => string.Equals(
                AnnotationNameIdentity.GetDimensionName(actual),
                expectedDimension,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return sameDimension.Count == 1
            ? sameDimension
            : Array.Empty<string>();
    }
}

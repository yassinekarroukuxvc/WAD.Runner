using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

public sealed class AnnotationCleanupExecutor
{
    private readonly AnnotationCleanupPlanner _planner;
    private readonly DrawingAnnotationStateReader _stateReader;
    private readonly AnnotationDiffService _diffService;

    public AnnotationCleanupExecutor(
        AnnotationCleanupPlanner planner,
        DrawingAnnotationStateReader stateReader,
        AnnotationDiffService diffService)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _stateReader = stateReader ?? throw new ArgumentNullException(nameof(stateReader));
        _diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
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
            Logger.Warn($"[{logPrefix}.Plan] Planned deletions = 0. Dumping existing dimensions for diagnostics.");
            _diffService.DumpExisting(existingByView, logPrefix);
            return 0;
        }

        _diffService.DumpDeletionPlan($"{logPrefix} Cleanup Runner", deletions, logPrefix);

        var totalDeleted = 0;
        foreach (var group in deletions.GroupBy(d => d.ViewName, StringComparer.OrdinalIgnoreCase))
        {
            var fullNames = group.Select(x => x.AnnotationFullName).ToList();
            var deletedInView = AnnotationCleanupService.RemoveDimensionsByFullNamesInView(
                drawingService,
                nameMap,
                logicalViewName: group.Key,
                fullNames: fullNames);

            totalDeleted += deletedInView;
            Logger.Info($"[{logPrefix}.Cleanup] Deleted in view '{group.Key}': {deletedInView}/{fullNames.Count} planned.");
        }

        Logger.Info($"[{logPrefix}.Cleanup] Total deleted annotations: {totalDeleted}");
        return totalDeleted;
    }
}

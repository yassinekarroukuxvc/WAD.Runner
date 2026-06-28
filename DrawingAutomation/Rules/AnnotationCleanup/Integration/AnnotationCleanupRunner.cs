using System;
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;
using WAD.Runner.DrawingAutomation.Rules.Common;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Integration;

public sealed class AnnotationCleanupRunner : IDrawingCleanupRunner
{
    private readonly AnnotationCleanupContextFactory _contextFactory;
    private readonly AnnotationCleanupExecutor _executor;

    public AnnotationCleanupRunner(WedgeType appliesTo)
        : this(
            appliesTo,
            new AnnotationCleanupContextFactory(),
            new AnnotationCleanupExecutor(
                new AnnotationCleanupPlanner(AnnotationRuleCatalogRegistry.CreateDefault()),
                new DrawingAnnotationStateReader(),
                new AnnotationDiffService()))
    {
    }

    public AnnotationCleanupRunner(
        WedgeType appliesTo,
        AnnotationCleanupContextFactory contextFactory,
        AnnotationCleanupExecutor executor)
    {
        AppliesTo = appliesTo;
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public WedgeType AppliesTo { get; }

    public void TryApply(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        DrawingRun run,
        DrawingData drawingData,
        bool activateEachView = true)
    {
        try
        {
            if (run is null) throw new ArgumentNullException(nameof(run));
            if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

            if (run.WedgeType != AppliesTo)
            {
                Logger.Info($"[{AppliesTo}.Cleanup] Skipped because run wedge type is {run.WedgeType}, not {AppliesTo}.");
                return;
            }

            var logPrefix = AppliesTo.ToString();
            var ctx = _contextFactory.Create(run, drawingData, nameMap, logPrefix);
            _executor.Apply(ds, nameMap, ctx, activateEachView, logPrefix);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[{AppliesTo}.Cleanup] Failed; continuing drawing automation. {ex.Message}");
        }
    }
}

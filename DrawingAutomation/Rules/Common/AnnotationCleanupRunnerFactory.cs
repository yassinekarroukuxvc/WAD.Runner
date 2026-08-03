using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Wedges;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Integration;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

public static class AnnotationCleanupRunnerFactory
{
    private static readonly AnnotationCleanupContextFactory ContextFactory = new();

    private static readonly AnnotationCleanupExecutor Executor = new(
        new AnnotationCleanupPlanner(AnnotationRuleCatalogRegistry.CreateDefault()),
        new DrawingAnnotationStateReader(),
        new AnnotationDiffService());

    private static readonly IReadOnlyDictionary<WedgeType, IDrawingCleanupRunner> Registry =
        DrawingWedgeModuleRegistry.SupportedWedgeTypes
            .ToDictionary(
                wedgeType => wedgeType,
                wedgeType => (IDrawingCleanupRunner)new AnnotationCleanupRunner(
                    wedgeType,
                    ContextFactory,
                    Executor));

    public static IDrawingCleanupRunner? TryGet(WedgeType wedgeType)
        => Registry.TryGetValue(wedgeType, out var runner)
            ? runner
            : null;

}

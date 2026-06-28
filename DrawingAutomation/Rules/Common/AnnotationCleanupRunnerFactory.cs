using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Integration;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

public static class AnnotationCleanupRunnerFactory
{
    private static readonly IReadOnlyDictionary<WedgeType, IDrawingCleanupRunner> Registry =
        new Dictionary<WedgeType, IDrawingCleanupRunner>
        {
            [WedgeType.CKVD] = new AnnotationCleanupRunner(WedgeType.CKVD),
            [WedgeType.COB] = new AnnotationCleanupRunner(WedgeType.COB),
            [WedgeType.UTUS] = new AnnotationCleanupRunner(WedgeType.UTUS),
            [WedgeType.FP] = new AnnotationCleanupRunner(WedgeType.FP),
            [WedgeType.OSG7] = new AnnotationCleanupRunner(WedgeType.OSG7)
        };

    public static IDrawingCleanupRunner? TryGet(WedgeType wedgeType)
        => Registry.TryGetValue(wedgeType, out var runner) ? runner : null;

    public static bool HasRunner(WedgeType wedgeType)
        => Registry.ContainsKey(wedgeType);
}

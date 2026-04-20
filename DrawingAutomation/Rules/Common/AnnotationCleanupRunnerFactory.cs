// DrawingAutomation/Rules/Common/AnnotationCleanupRunnerFactory.cs
using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.COB;
using WAD.Runner.DrawingAutomation.Rules.FP;
using WAD.Runner.DrawingAutomation.Rules.UTUS;

namespace WAD.Runner.DrawingAutomation.Rules.Common;

/// <summary>
/// Maps WedgeType → IDrawingCleanupRunner.
/// </summary>
public static class AnnotationCleanupRunnerFactory
{
    private static readonly IReadOnlyDictionary<WedgeType, IDrawingCleanupRunner> Registry =
        new Dictionary<WedgeType, IDrawingCleanupRunner>
        {
            [WedgeType.COB] = new CobAnnotationCleanupRunner(),
            [WedgeType.UTUS] = new UtusAnnotationCleanupRunner(),
            [WedgeType.FP] = new FpAnnotationCleanupRunner(),
            // add new types here
        };

    /// <summary>
    /// Returns the runner for the given wedge type, or null if this type has no cleanup runner.
    /// </summary>
    public static IDrawingCleanupRunner? TryGet(WedgeType wedgeType)
        => Registry.TryGetValue(wedgeType, out var runner) ? runner : null;

    /// <summary>
    /// Returns true if a runner is registered for this wedge type.
    /// </summary>
    public static bool HasRunner(WedgeType wedgeType)
        => Registry.ContainsKey(wedgeType);
}

// DrawingAutomation/Rules/COB/CobAnnotationCleanupRunner.cs
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.COB;

/// <summary>
/// COB annotation cleanup runner.
/// All logic is in <see cref="BaseAnnotationCleanupRunner"/> and <see cref="SharedAnnotationDeletionRules"/>.
/// COB, UTUS, and FP share identical template annotation structures, so the rules are identical.
/// </summary>
public sealed class CobAnnotationCleanupRunner : BaseAnnotationCleanupRunner
{
    public override WedgeType AppliesTo => WedgeType.COB;
    protected override string LogPrefix => "COB";

    protected override IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> PlanDeletions(
        ModelDoc2 model,
        SharedAnnotationDeletionRules.DrawingType drawingType,
        SharedAnnotationDeletionRules.ShankType shank,
        SharedAnnotationDeletionRules.FootOption foot,
        SharedAnnotationDeletionRules.Options options,
        SharedAnnotationDeletionRules.ViewNameMap viewNames,
        bool activateEachView)
        => SharedAnnotationDeletionRules.PlanDeletionsFromDrawing(
            model, drawingType, shank, foot, options, viewNames, activateEachView);
}

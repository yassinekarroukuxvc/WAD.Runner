// DrawingAutomation/Rules/UTUS/UtusAnnotationCleanupRunner.cs
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.UTUS;

/// <summary>UTUS annotation cleanup runner. Delegates to <see cref="SharedAnnotationDeletionRules"/>.</summary>
public sealed class UtusAnnotationCleanupRunner : BaseAnnotationCleanupRunner
{
    public override WedgeType AppliesTo => WedgeType.UTUS;
    protected override string LogPrefix => "UTUS";

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

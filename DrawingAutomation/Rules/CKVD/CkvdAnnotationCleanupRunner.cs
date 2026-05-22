// DrawingAutomation/Rules/CKVD/CkvdAnnotationCleanupRunner.cs
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.CKVD;

/// <summary>
/// CKVD annotation cleanup runner.
/// CKVD now follows the same runner-based cleanup flow as COB / FP / UTUS:
/// scan the active drawing views, build the CKVD keep-set, and delete everything
/// that is not explicitly kept by the CKVD drawing rules.
/// </summary>
public sealed class CkvdAnnotationCleanupRunner : BaseAnnotationCleanupRunner
{
    public override WedgeType AppliesTo => WedgeType.CKVD;
    protected override string LogPrefix => "CKVD";

    protected override IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> PlanDeletions(
        ModelDoc2 model,
        SharedAnnotationDeletionRules.DrawingType drawingType,
        SharedAnnotationDeletionRules.ShankType shank,
        SharedAnnotationDeletionRules.FootOption foot,
        SharedAnnotationDeletionRules.Options options,
        SharedAnnotationDeletionRules.ViewNameMap viewNames,
        bool activateEachView)
        => CkvdAnnotationDeletionRules.PlanDeletionsFromDrawing(
            model, drawingType, shank, foot, options, viewNames, activateEachView);
}

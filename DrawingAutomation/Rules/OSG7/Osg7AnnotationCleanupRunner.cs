// DrawingAutomation/Rules/OSG7/Osg7AnnotationCleanupRunner.cs
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Rules.Common;

namespace WAD.Runner.DrawingAutomation.Rules.OSG7;

/// <summary>
/// OSG7 annotation cleanup runner.
/// Uses the same runner-based production/customer cleanup flow as COB / FP / UTUS / CKVD.
/// </summary>
public sealed class Osg7AnnotationCleanupRunner : BaseAnnotationCleanupRunner
{
    public override WedgeType AppliesTo => WedgeType.OSG7;
    protected override string LogPrefix => "OSG7";

    protected override IReadOnlyList<SharedAnnotationDeletionRules.DeletionTarget> PlanDeletions(
        ModelDoc2 model,
        SharedAnnotationDeletionRules.DrawingType drawingType,
        SharedAnnotationDeletionRules.ShankType shank,
        SharedAnnotationDeletionRules.FootOption foot,
        SharedAnnotationDeletionRules.Options options,
        SharedAnnotationDeletionRules.ViewNameMap viewNames,
        bool activateEachView)
        => Osg7AnnotationDeletionRules.PlanDeletionsFromDrawing(
            model, drawingType, shank, foot, options, viewNames, activateEachView);
}

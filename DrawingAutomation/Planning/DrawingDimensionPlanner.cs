using System;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DrawingAutomation;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Planning;


public static class DrawingDimensionPlanner
{
    public static PlannedDrawingDimensions Plan(DrawingRun run, WAD.Runner.DataManagement.Domain.Drawing.DrawingData drawingData)
    {
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));

        var context = new LayoutContext(run.Wedge, drawingData);
        var diagnostics = new PlannerDiagnostics();
        var dimensions = DimensionRules.Build(context, diagnostics, run.WedgeType).ToList();

        var plans = dimensions.Select(d => new AnnotationPositioner.Plan
        {
            Id = d.Id,
            View = d.View,
            Key = d.Key,
            PositionMm = d.PositionMm,
            Nominal = d.Nominal
        }).ToList();

        Logger.Info($"[DrawingPlan] Planned {plans.Count} annotation positions for {run.WedgeType}/{drawingData.DrawingType}.");

        return new PlannedDrawingDimensions
        {
            Context = context,
            Dimensions = dimensions,
            AnnotationPlans = plans
        };
    }
}

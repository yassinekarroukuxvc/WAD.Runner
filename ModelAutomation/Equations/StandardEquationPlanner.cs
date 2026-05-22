using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.ModelAutomation.Core;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Equations;

public class StandardEquationPlanner : IEquationPlanner
{
    public virtual EquationPlan Build(ModelAutomationContext context)
    {
        var wedge = context.Wedge
            ?? throw new System.InvalidOperationException("WedgeData is required to build equations.");

        var builder = new EquationPlanBuilder()
            .WithDimensions(wedge.Dimensions, EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions();

        AddEngravingStart(builder, context);
        AddOverlayScale(builder, context);

        return builder.Build();
    }

    private protected static void AddEngravingStart(
        EquationPlanBuilder builder,
        ModelAutomationContext context)
    {
        var wedge = context.Wedge;
        if (wedge is null) return;

        decimal engravingMm = 0m;

        if (wedge.KValue is not null)
            engravingMm = wedge.KValue.ValueMm.AsMm();
        else if (context.Facts?.TryGetLengthMm("TL", out var tl) == true)
            engravingMm = tl * 0.40m;

        builder.AddManaged(
            EquationCatalog.Names.EngravingStart,
            EquationFormatting.Line(
                EquationCatalog.Names.EngravingStart,
                (double)engravingMm,
                "mm"));
    }

    private protected static void AddOverlayScale(
        EquationPlanBuilder builder,
        ModelAutomationContext context)
    {
        if (context.DrawingType != DrawingType.Overlay || context.Facts is null)
            return;

        var mag = EquationGeometry.OverlayMagnification(context.Facts, context.WedgeType);
        var scale = EquationGeometry.OverlayScaleDecimal(mag);

        builder.AddManaged(
            EquationCatalog.Names.OverlayCalibration1,
            EquationFormatting.Line(EquationCatalog.Names.OverlayCalibration1, mag));

        builder.AddManaged(
            EquationCatalog.Names.Scale,
            EquationFormatting.Line(EquationCatalog.Names.Scale, scale));

        builder.AddManaged(
            "TL",
            EquationFormatting.Line("TL", 30.0, "mm"));
    }
}
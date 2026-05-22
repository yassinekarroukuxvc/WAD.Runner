using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class CkvdEquationPlanner : StandardEquationPlanner
{
    public override EquationPlan Build(ModelAutomationContext context)
    {
        var wedge = context.Wedge ?? throw new System.InvalidOperationException("WedgeData is required to build CKVD equations.");
        var builder = new EquationPlanBuilder()
            .WithDimensions(wedge.Dimensions, EquationCatalog.DbToModelAliases)
            .WriteProvidedZeros()
            .ZeroMissingKeys(EquationCatalog.CkvdDbDrivenKeys, EquationCatalog.CkvdAngleKeys);

        AddEngravingStart(builder, context);
        AddOverlayScale(builder, context);
        return builder.Build();
    }
}

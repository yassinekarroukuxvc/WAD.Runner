using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.ModelAutomation.Core;
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class Osg7EquationPlanner : StandardEquationPlanner
{
    public override EquationPlan Build(ModelAutomationContext context)
    {
        var wedge = context.Wedge ?? throw new System.InvalidOperationException("WedgeData is required to build OSG7 equations.");
        var dims = new Dictionary<DimensionKey, DomDim>(wedge.Dimensions);
        CopyIfMissingOrZero(dims, "FRX", "FR");
        CopyIfMissingOrZero(dims, "BRX", "BR");

        var builder = new EquationPlanBuilder()
            .WithDimensions(dims, EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions();

        AddEngravingStart(builder, context);
        AddOverlayScale(builder, context);
        return builder.Build();
    }

    private static void CopyIfMissingOrZero(IDictionary<DimensionKey, DomDim> dims, string targetKey, string sourceKey)
    {
        var t = DimensionKey.From(targetKey);
        var s = DimensionKey.From(sourceKey);
        var missing = !dims.TryGetValue(t, out var target) || target is null;
        var zero = !missing && target!.Nominal.Value == 0m;
        if (!(missing || zero)) return;
        if (dims.TryGetValue(s, out var source) && source is not null) dims[t] = source;
    }
}

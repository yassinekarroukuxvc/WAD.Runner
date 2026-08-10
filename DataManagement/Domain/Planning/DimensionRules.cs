using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning;

public static class DimensionRules
{
    public static List<DimensionSpec> Build(LayoutContext ctx, PlannerDiagnostics diag)
        => Build(ctx, diag, WedgeType.CKVD);
    public static List<DimensionSpec> Build(
        LayoutContext ctx,
        PlannerDiagnostics diag,
        WedgeType wedgeType)
    {
        return wedgeType switch
        {
            WedgeType.CKVD => Rules.CkvdDimensionRules.Build(ctx, diag),
            WedgeType.COB => Rules.CobDimensionRules.Build(ctx, diag),
            WedgeType.UTUS => Rules.UtusDimensionRules.Build(ctx, diag),
            WedgeType.FP => Rules.FpDimensionRules.Build(ctx, diag),
            WedgeType.OSG7 => Rules.Osg7DimensionRules.Build(ctx, diag),
            WedgeType._4516 => Rules._4516DimensionRules.Build(ctx,diag),
            _ => Rules.CkvdDimensionRules.Build(ctx, diag)
        };
    }
}

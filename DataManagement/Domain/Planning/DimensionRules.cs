// Domain/Planning/DimensionRules.cs
using WAD.Runner.Application;
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
        var drawingType = ctx.Drawing.DrawingType;

        Logger.Info($"[Plan] Enter DimensionRules.Build (wedgeType={wedgeType}, dtype={drawingType})");

        return wedgeType switch
        {
            WedgeType.CKVD => Rules.CkvdDimensionRules.Build(ctx, diag),
            WedgeType.COB => Rules.CobDimensionRules.Build(ctx, diag),
            WedgeType.UTUS => Rules.UtusDimensionRules.Build(ctx, diag),
            WedgeType.FP => Rules.FpDimensionRules.Build(ctx, diag),

            _ => Rules.CkvdDimensionRules.Build(ctx, diag)
        };
    }
}

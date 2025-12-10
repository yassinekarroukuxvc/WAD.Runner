// Domain/Planning/Rules/CobDimensionRules.cs
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

/// <summary>
/// COB-specific dimension rules.
/// Currently delegates to CKVD rules for backward compatibility.
/// Replace methods with real COB logic when ready.
/// </summary>
internal static class CobDimensionRules
{
    public static List<DimensionSpec> Build(LayoutContext ctx, PlannerDiagnostics diag)
    {
        Logger.Info($"[Plan] Enter CobDimensionRules.Build (dtype={ctx.Drawing.DrawingType})");

        // TODO: Implement true COB-specific rules.
        // For now, reuse CKVD rules so behavior is unchanged.
        return CkvdDimensionRules.Build(ctx, diag);
    }
}

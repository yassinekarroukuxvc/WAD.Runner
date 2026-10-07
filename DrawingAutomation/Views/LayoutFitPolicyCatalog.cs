using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Per-wedge-type layout fit policies. Any wedge type without an
/// entry (including Unknown and Other) uses LayoutFitPolicy.Default.
/// </summary>
public static class LayoutFitPolicyCatalog
{
    private static readonly IReadOnlyDictionary<WedgeType, LayoutFitPolicy> Policies =
        new Dictionary<WedgeType, LayoutFitPolicy>
        {
            [WedgeType.CKVD] = LayoutFitPolicy.Default with
            {
                MaxSecondaryIterations = 70
            },
            [WedgeType.COB] = LayoutFitPolicy.Default,
            [WedgeType.OSG7] = LayoutFitPolicy.Default with
            {
                MaxSecondaryIterations = 70
            },
            [WedgeType.UTUS] = LayoutFitPolicy.Default,
            [WedgeType.FP] = LayoutFitPolicy.Default,
            [WedgeType._4516] = LayoutFitPolicy.Default,
            [WedgeType.ABT] = LayoutFitPolicy.Default,
            [WedgeType.AB16] = LayoutFitPolicy.Default,
            [WedgeType._45CK] = LayoutFitPolicy.Default,
            [WedgeType.M] = LayoutFitPolicy.Default,
            [WedgeType._1001] = LayoutFitPolicy.Default,
            [WedgeType.VM] = LayoutFitPolicy.Default
        };

    public static LayoutFitPolicy For(
        WedgeType wedgeType)
        => Policies.TryGetValue(
            wedgeType,
            out var policy)
            ? policy
            : LayoutFitPolicy.Default;
}
using System.Collections.Generic;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Core;
using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class CobLikeEquationPlanner : StandardEquationPlanner
{
    private const decimal DefaultVraDeg = 90m;
    private readonly WedgeType _wedgeType;

    public CobLikeEquationPlanner(WedgeType wedgeType)
    {
        _wedgeType = wedgeType;
    }

    public override EquationPlan Build(ModelAutomationContext context)
    {
        var wedge = context.Wedge ?? throw new System.InvalidOperationException("WedgeData is required to build COB-like equations.");
        var facts = context.Facts ?? new WedgeFacts(wedge);
        var dims = new Dictionary<DimensionKey, DomDim>(wedge.Dimensions);

        ApplyVraDefault(facts, dims);
        var funnelGap = EquationGeometry.FunnelGapMmOrDefault(facts);
        UpsertLengthMm(dims, EquationCatalog.Names.FunnelGap, funnelGap);

        var builder = new EquationPlanBuilder()
            .WithDimensions(dims, EquationCatalog.DbToModelAliases)
            .SkipProvidedZeroDimensions();

        AddEngravingStart(builder, context);
        AddOverlayScale(builder, context);
        builder.AddManaged(EquationCatalog.Names.FunnelGap, EquationFormatting.Line(EquationCatalog.Names.FunnelGap, (double)funnelGap, "mm"));

        var rawCut = EquationGeometry.NonStdCutRawMm(facts);
        var finalCut = rawCut;
        if (context.DrawingType == DrawingType.Overlay)
        {
            var mag = EquationGeometry.OverlayMagnification(facts, _wedgeType);
            var scale = EquationGeometry.OverlayScaleDecimal(mag);
            finalCut = EquationGeometry.OverlaySafeNonStdCutMm(rawCut, scale, _wedgeType);
        }

        builder.AddManaged(EquationCatalog.Names.NonStdCut, EquationFormatting.Line(EquationCatalog.Names.NonStdCut, (double)finalCut, "mm"));
        Logger.Info($"[CobLikeEquationPlanner] {_wedgeType}: funnel_gap={funnelGap}mm, non_std_cut={finalCut}mm");

        return builder.Build();
    }

    private static void ApplyVraDefault(WedgeFacts facts, IDictionary<DimensionKey, DomDim> dims)
    {
        var hasVFamily = facts.HasPositive("VW") || facts.HasPositive("VRR") || facts.HasPositive("VR");
        if (!hasVFamily) return;

        var vraKey = DimensionKey.From("VRA");
        var missing = !dims.TryGetValue(vraKey, out var vra) || vra is null;
        var zero = !missing && vra!.Nominal.Value == 0m;
        if (missing || zero) UpsertAngleDeg(dims, "VRA", DefaultVraDeg);
    }

    private static void UpsertLengthMm(IDictionary<DimensionKey, DomDim> dims, string key, decimal mm)
    {
        var dk = DimensionKey.From(key);
        dims[dk] = DomDim.CreateLength(dk, Quantity.MmOf(mm), Tolerance.Zero, null);
    }

    private static void UpsertAngleDeg(IDictionary<DimensionKey, DomDim> dims, string key, decimal deg)
    {
        var dk = DimensionKey.From(key);
        dims[dk] = DomDim.CreateAngle(dk, Quantity.DegOf(deg), null);
    }
}

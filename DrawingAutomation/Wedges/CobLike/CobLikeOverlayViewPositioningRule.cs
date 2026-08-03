using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges.CobLike;

public sealed class CobLikeOverlayViewPositioningRule
    : OverlayViewPositioningRuleBase
{
    public override string Name => "COB-like overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        var baseReferencePoint = context.BaseReferencePointName;
        var detailReferencePoint = baseReferencePoint;

        if (context.HasPositiveLength("VW") &&
            context.HasPositiveLength("VR"))
        {
            detailReferencePoint = OverlayReferencePointNames.GenericNonStandardCut;
            Logger.Info(
                "[Overlay] COB-like wedge with VW>0 and VR>0 -> " +
                $"Detail uses '{detailReferencePoint}'.");
        }

        return BuildStandardPlacements(
            context,
            detailReferencePoint,
            baseReferencePoint,
            detailYIn: OverlayPositioningDefaults.DetailYIn,
            sectionYIn: ResolveSectionYIn(context));
    }

    private static double ResolveSectionYIn(
        OverlayViewPositioningContext context)
    {
        if (context.IsReverse180)
        {
            Logger.Info(
                "[Overlay] COB-like Reverse-180 -> " +
                $"Section Y remains {OverlayPositioningDefaults.SectionYIn:0.####} in.");

            return OverlayPositioningDefaults.SectionYIn;
        }

        var tdfMm = context.GetLengthMmOrNaN("TDF");
        var tdMm = context.GetLengthMmOrNaN("TD");

        if (!IsPositiveFinite(tdfMm) || !IsPositiveFinite(tdMm))
        {
            Logger.Warn(
                "[Overlay] Missing/invalid dimensions for COB-like Section Y. " +
                $"TDF={tdfMm:0.####} mm, TD={tdMm:0.####} mm. " +
                $"Falling back to {OverlayPositioningDefaults.SectionYIn:0.####} in.");

            return OverlayPositioningDefaults.SectionYIn;
        }

        var scaledTdfMm = tdfMm * context.OverlayScale;
        var scaledTdMm = tdMm * context.OverlayScale;
        var computedYmm =
            OverlayPositioningConstants.DetailSectionBaselineMm -
            ((scaledTdfMm - (scaledTdMm / 2.0)) / 2.0);

        if (!IsPositiveFinite(computedYmm))
        {
            Logger.Warn(
                $"[Overlay] Computed COB-like Section Y was invalid ({computedYmm:0.####} mm). " +
                "Falling back to the default Section Y.");

            return OverlayPositioningDefaults.SectionYIn;
        }

        var computedYIn = MmToIn(computedYmm);
        Logger.Info($"[Overlay] COB-like standard shank -> Section Y={computedYIn:0.####} in.");
        return computedYIn;
    }
}

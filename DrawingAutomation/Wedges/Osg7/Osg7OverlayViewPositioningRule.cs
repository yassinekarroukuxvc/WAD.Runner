using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;

namespace WAD.Runner.DrawingAutomation.Wedges.Osg7;

public sealed class Osg7OverlayViewPositioningRule
    : OverlayViewPositioningRuleBase
{
    public override string Name => "OSG7 overlay positioning";

    public override IReadOnlyList<OverlayViewPlacement> BuildPlacements(
        OverlayViewPositioningContext context)
    {
        var referencePoint = context.BaseReferencePointName;

        return BuildStandardPlacements(
            context,
            referencePoint,
            referencePoint,
            detailYIn: OverlayPositioningDefaults.DetailYIn,
            sectionYIn: ResolveSectionYIn(context));
    }

    private static double ResolveSectionYIn(
        OverlayViewPositioningContext context)
    {
        var tdfMm = context.GetLengthMmOrNaN("TDF");
        var tdMm = context.GetLengthMmOrNaN("TD");
        var fxMm = context.GetLengthMmOrNaN("FX");
        var xMm = context.GetLengthMmOrNaN("X");
        var flMm = context.GetLengthMmOrNaN("FL");

        if (!IsPositiveFinite(tdfMm) ||
            !IsPositiveFinite(tdMm) ||
            !IsPositiveFinite(flMm))
        {
            Logger.Warn(
                "[Overlay][OSG7] Missing/invalid TDF, TD, or FL. " +
                $"TDF={tdfMm:0.####}, TD={tdMm:0.####}, FL={flMm:0.####}. " +
                $"Falling back to {OverlayPositioningDefaults.SectionYIn:0.####} in.");

            return OverlayPositioningDefaults.SectionYIn;
        }

        if (!IsNonNegativeFinite(fxMm))
        {
            if (IsNonNegativeFinite(xMm))
            {
                fxMm = tdfMm - (xMm + flMm);
            }
            else
            {
                Logger.Warn(
                    "[Overlay][OSG7] FX and X are both missing or invalid. " +
                    "Falling back to the default Section Y.");

                return OverlayPositioningDefaults.SectionYIn;
            }
        }

        if (!IsNonNegativeFinite(fxMm))
        {
            Logger.Warn(
                $"[Overlay][OSG7] Resolved FX={fxMm:0.####} mm is invalid. " +
                "Falling back to the default Section Y.");

            return OverlayPositioningDefaults.SectionYIn;
        }

        var scaledTdfMm = tdfMm * context.OverlayScale;
        var scaledTdMm = tdMm * context.OverlayScale;
        var scaledFxMm = fxMm * context.OverlayScale;
        var scaledFlMm = flMm * context.OverlayScale;

        var centerOfPartMm = scaledTdMm / 2.0;
        var centerOfViewMm = scaledTdfMm - scaledFxMm - (scaledFlMm / 2.0);
        var computedYmm =
            OverlayPositioningConstants.DetailSectionBaselineMm +
            (centerOfPartMm - centerOfViewMm);

        if (!IsPositiveFinite(computedYmm))
        {
            Logger.Warn(
                $"[Overlay][OSG7] Computed Section Y was invalid ({computedYmm:0.####} mm). " +
                "Falling back to the default Section Y.");

            return OverlayPositioningDefaults.SectionYIn;
        }

        var computedYIn = MmToIn(computedYmm);
        Logger.Info(
            "[Overlay][OSG7] Section Y calculation -> " +
            $"TDF={tdfMm:0.####} mm, TD={tdMm:0.####} mm, " +
            $"FX={fxMm:0.####} mm, FL={flMm:0.####} mm, " +
            $"scale={context.OverlayScale:0.####}, Y={computedYIn:0.####} in.");

        return computedYIn;
    }
}

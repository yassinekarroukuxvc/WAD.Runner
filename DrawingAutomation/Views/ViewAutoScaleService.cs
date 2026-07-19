using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Compatibility autoscale facade.
///
/// Production drawings should use DrawingViewLayoutCoordinator because it
/// evaluates Front breakline geometry together with candidate scales.
///
/// This class intentionally does NOT write the runtime scale back into
/// DrawingData.
/// </summary>
public sealed class ViewAutoScaleService
{
    public sealed record Policy(
        double FillRatioHeight,
        double MinScale,
        double MaxScale,
        double Step,
        double TopMarginMm = 0.0,
        double BottomMarginMm = 0.0);

    private readonly DrawingService _drawingService;

    public ViewAutoScaleService(
        DrawingService drawingService)
    {
        _drawingService =
            drawingService
            ?? throw new ArgumentNullException(
                nameof(drawingService));
    }

    public double ApplyUnifiedScaleFromFront(
        DrawingData drawingData,
        Policy policy,
        IDictionary<string, string>? nameMap = null)
    {
        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        if (policy is null)
            throw new ArgumentNullException(nameof(policy));

        ValidatePolicy(
            policy);

        var scales =
            new ViewScaleService(
                _drawingService,
                nameMap);

        var geometry =
            new ViewGeometryService(
                _drawingService,
                nameMap);

        var best =
            policy.MinScale;

        var candidate =
            policy.MaxScale;

        while (candidate >=
               policy.MinScale - 1e-9)
        {
            var normalized =
                Math.Max(
                    candidate,
                    policy.MinScale);

            scales.ApplyScale(
                "Front",
                normalized);

            _drawingService.Rebuild();

            if (geometry.FitsHeight(
                    "Front",
                    policy.FillRatioHeight,
                    policy.TopMarginMm,
                    policy.BottomMarginMm))
            {
                best =
                    normalized;

                break;
            }

            candidate -=
                policy.Step;
        }

        scales.ApplyUnifiedScale(
            new[]
            {
                "Front",
                "Side",
                "Top"
            },
            best);

        _drawingService.Rebuild();

        Logger.Info(
            $"[AutoScale] Unified primary scale = {best:0.###}. " +
            "DrawingData was not mutated.");

        return best;
    }

    private static void ValidatePolicy(
        Policy policy)
    {
        if (!double.IsFinite(policy.MinScale) ||
            policy.MinScale <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy.MinScale));
        }

        if (!double.IsFinite(policy.MaxScale) ||
            policy.MaxScale < policy.MinScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy.MaxScale));
        }

        if (!double.IsFinite(policy.Step) ||
            policy.Step <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy.Step));
        }

        if (!double.IsFinite(policy.FillRatioHeight) ||
            policy.FillRatioHeight <= 0.0 ||
            policy.FillRatioHeight > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy.FillRatioHeight));
        }

        if (!double.IsFinite(policy.TopMarginMm) ||
            !double.IsFinite(policy.BottomMarginMm) ||
            policy.TopMarginMm < 0.0 ||
            policy.BottomMarginMm < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy));
        }
    }
}

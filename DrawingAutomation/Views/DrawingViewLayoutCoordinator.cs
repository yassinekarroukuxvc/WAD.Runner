using System;
using System.Collections.Generic;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// The single owner of drawing-view layout sequencing.
///
/// Responsibilities are intentionally separated:
///
/// ViewPositionService  -> positions only
/// ViewScaleService     -> scales only
/// ViewGeometryService  -> read-only measurements
/// BreaklineService     -> breaklines only
///
/// This coordinator owns the order and rebuild boundaries.
/// </summary>
public sealed class DrawingViewLayoutCoordinator
{
    private readonly DrawingService _drawingService;
    private readonly ViewPositionService _positions;
    private readonly ViewScaleService _scales;
    private readonly ViewGeometryService _geometry;
    private readonly BreaklineService _breaklines;

    public DrawingViewLayoutCoordinator(
        DrawingService drawingService,
        IDictionary<string, string>? logicalToActual = null)
    {
        _drawingService =
            drawingService
            ?? throw new ArgumentNullException(
                nameof(drawingService));

        _positions =
            new ViewPositionService(
                drawingService,
                logicalToActual);

        _scales =
            new ViewScaleService(
                drawingService,
                logicalToActual);

        _geometry =
            new ViewGeometryService(
                drawingService,
                logicalToActual);

        _breaklines =
            new BreaklineService(
                drawingService,
                logicalToActual);
    }

    public ViewLayoutResult Apply(
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        Logger.Info(
            "[ViewLayout] Starting layout stabilization...");

        /*
         * 1. Prepare movement exactly once.
         *
         * We do not repeatedly break alignment every time
         * a position is set.
         */
        _positions.PrepareForMovement(
            DrawingViewNames.LayoutOrder);

        /*
         * 2. Detail and Section use their normal configured scales.
         *
         * This fixes the previous hidden dependency on whatever scale
         * happened to exist in the template.
         */
        _scales.ApplyConfiguredScales(
            drawingData,
            DrawingViewNames.FixedScale);

        _drawingService.Rebuild();

        /*
         * 3. Find the unified Front/Side/Top autoscale.
         *
         * Front drives the fit calculation.
         *
         * If Front uses a breakline, that breakline is refreshed at each
         * candidate scale before measuring the final visible outline.
         *
         * CKVD Production/Customer profiles do not include Front or Side
         * in BreaklineViews, so those breaklines are not managed here.
         */
        var primaryScale =
            FindPrimaryScale(
                run,
                drawingData,
                profile);

        /*
         * 4. Establish the final unified primary scale.
         */
        _scales.ApplyUnifiedScale(
            DrawingViewNames.Primary,
            primaryScale);

        /*
         * 5. Recalculate every enabled breakline from FINAL scales.
         *
         * At this point:
         *
         * Front / Side / Top = final autoscale
         * Detail / Section   = configured normal scale
         *
         * The active profile decides which views use breaklines.
         * For CKVD Production/Customer, only Detail and Section are
         * managed because the CKVD profile uses SecondaryBreaklineViews.
         */
        _breaklines.ApplyEnabled(
            run.WedgeType,
            run.Wedge,
            drawingData,
            profile);

        _drawingService.Rebuild();

        /*
         * 6. Apply configured positions after final scale and breakline
         * geometry are stable.
         *
         * PositionMm normally represents the SolidWorks view origin.
         */
        _positions.ApplyConfiguredPositions(
            drawingData,
            DrawingViewNames.LayoutOrder);

        _drawingService.Rebuild();

        /*
         * 7. CKVD Front and Side are intentionally unbroken in
         * Production/Customer drawings.
         *
         * Their full visible geometry is vertically offset from the
         * SolidWorks view origin. For these two views only, interpret the
         * configured PositionMm Y as the desired visible-outline center Y.
         *
         * X remains origin-based and is not changed.
         * Detail and Section are not touched and continue using their
         * normal configured origin positions and managed breaklines.
         */
        if (RequiresCkvdPrimaryVisibleCenterCorrection(
                run,
                profile))
        {
            _positions.AlignVisibleCenterYToConfiguredPosition(
                DrawingViewNames.Front,
                drawingData);

            _positions.AlignVisibleCenterYToConfiguredPosition(
                DrawingViewNames.Side,
                drawingData);

            _drawingService.Rebuild();
        }

        var finalScales =
            CaptureFinalScales(
                drawingData);

        Logger.Success(
            $"[ViewLayout] Layout stabilized. " +
            $"Primary unified scale = {primaryScale:0.###}.");

        return new ViewLayoutResult(
            primaryScale,
            finalScales);
    }

    private IReadOnlyDictionary<string, double> CaptureFinalScales(
        DrawingData drawingData)
    {
        var result =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var logicalView in DrawingViewNames.LayoutOrder)
        {
            if (_scales.TryGetCurrentScale(
                    logicalView,
                    out var currentScale))
            {
                result[logicalView] = currentScale;

                Logger.Info(
                    $"[ViewLayout] Final runtime scale " +
                    $"'{logicalView}' = {currentScale:0.###}.");

                continue;
            }

            if (drawingData.Views.TryGetValue(
                    logicalView,
                    out var configuredView)
                && configuredView is not null
                && double.IsFinite(configuredView.Scale)
                && configuredView.Scale > 0.0)
            {
                result[logicalView] = configuredView.Scale;

                Logger.Warn(
                    $"[ViewLayout] Could not read runtime scale for " +
                    $"'{logicalView}'. Falling back to configured scale " +
                    $"{configuredView.Scale:0.###}.");
            }
        }

        return result;
    }

    private double FindPrimaryScale(
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        var policy =
            profile.Scale;

        ValidatePolicy(
            policy);

        var candidate =
            policy.MaxScale;

        while (candidate >=
               policy.MinScale - 1e-9)
        {
            var normalized =
                Math.Max(
                    candidate,
                    policy.MinScale);

            /*
             * Only Front needs to change during candidate evaluation because
             * Front is the view used to measure fit.
             *
             * Side and Top receive the chosen scale once,
             * after the search.
             */
            _scales.ApplyScale(
                DrawingViewNames.Front,
                normalized);

            if (profile.UsesBreakline(
                    DrawingViewNames.Front))
            {
                /*
                 * The active wedge module is consulted because this
                 * breakline is recalculated for every candidate scale.
                 */
                _breaklines.Apply(
                    DrawingViewNames.Front,
                    run.WedgeType,
                    run.Wedge,
                    drawingData);
            }

            /*
             * Scale and breakline mutations must be regenerated before
             * reading the view outline.
             */
            _drawingService.Rebuild();

            if (_geometry.FitsHeight(
                    DrawingViewNames.Front,
                    policy))
            {
                Logger.Info(
                    $"[ViewLayout] Autoscale accepted " +
                    $"{normalized:0.###}.");

                return normalized;
            }

            candidate -=
                policy.Step;
        }

        Logger.Warn(
            $"[ViewLayout] No scale in range " +
            $"{policy.MinScale:0.###}..{policy.MaxScale:0.###} " +
            "satisfied the configured fill ratio. " +
            $"Using MinScale={policy.MinScale:0.###}.");

        return policy.MinScale;
    }

    private static bool RequiresCkvdPrimaryVisibleCenterCorrection(
        DrawingRun run,
        DrawingProfile profile)
    {
        if (run.WedgeType != WedgeType.CKVD)
            return false;

        if (profile.Key.DrawingType is not (
                DrawingType.Production or
                DrawingType.Customer))
        {
            return false;
        }

        /*
         * Do not compensate a view that the active profile deliberately
         * manages as a breakline view. This also keeps the rule safe if the
         * CKVD profile is changed again later.
         */
        return
            !profile.UsesBreakline(DrawingViewNames.Front)
            && !profile.UsesBreakline(DrawingViewNames.Side);
    }

    private static void ValidatePolicy(
        ScalePolicy policy)
    {
        if (policy is null)
            throw new ArgumentNullException(nameof(policy));

        if (!double.IsFinite(policy.MinScale) ||
            policy.MinScale <= 0.0)
        {
            throw new InvalidOperationException(
                "ScalePolicy.MinScale must be finite and > 0.");
        }

        if (!double.IsFinite(policy.MaxScale) ||
            policy.MaxScale < policy.MinScale)
        {
            throw new InvalidOperationException(
                "ScalePolicy.MaxScale must be finite and >= MinScale.");
        }

        if (!double.IsFinite(policy.Step) ||
            policy.Step <= 0.0)
        {
            throw new InvalidOperationException(
                "ScalePolicy.Step must be finite and > 0.");
        }

        if (!double.IsFinite(policy.FillRatioHeight) ||
            policy.FillRatioHeight <= 0.0 ||
            policy.FillRatioHeight > 1.0)
        {
            throw new InvalidOperationException(
                "ScalePolicy.FillRatioHeight must be > 0 and <= 1.");
        }

        if (!double.IsFinite(policy.TopMarginMm) ||
            !double.IsFinite(policy.BottomMarginMm) ||
            policy.TopMarginMm < 0.0 ||
            policy.BottomMarginMm < 0.0)
        {
            throw new InvalidOperationException(
                "ScalePolicy margins must be finite and >= 0.");
        }
    }
}

public sealed record ViewLayoutResult(
    double PrimaryScale,
    IReadOnlyDictionary<string, double> FinalScales);
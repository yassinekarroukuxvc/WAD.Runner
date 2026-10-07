using System;
using System.Collections.Generic;
using System.Linq;

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
    private readonly LayoutFitPolicy? _fitOverride;

    // Resolved at the start of Apply() from the override or the
    // per-wedge-type catalog.
    private LayoutFitPolicy _fit = LayoutFitPolicy.Default;

    public DrawingViewLayoutCoordinator(
        DrawingService drawingService,
        IDictionary<string, string>? logicalToActual = null,
        LayoutFitPolicy? fitPolicy = null)
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

        _fitOverride =
            fitPolicy;
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

        _fit =
            _fitOverride
            ?? LayoutFitPolicyCatalog.For(run.WedgeType);

        Logger.Info(
            $"[ViewLayout] Starting layout stabilization " +
            $"({run.WedgeType}: margin={_fit.SheetMarginMm:0.###} mm, " +
            $"gap={_fit.ViewGapMm:0.###} mm)...");

        /*
         * 1. Prepare movement exactly once.
         *
         * We do not repeatedly break alignment every time
         * a position is set.
         */
        _positions.PrepareForMovement(
            DrawingViewNames.LayoutOrder);

        /*
         * 2. Detail and Section start at their configured scales.
         * These are treated as MAXIMUM scales; they may be reduced
         * in step 8 if they do not fit.
         */
        _scales.ApplyConfiguredScales(
            drawingData,
            DrawingViewNames.FixedScale);

        _drawingService.Rebuild();

        /*
         * 3. Place views at their configured origins BEFORE searching
         * for the primary scale.
         *
         * Scaling happens about the view origin, so origins do not move
         * when the scale changes. Having final origins during the search
         * lets us measure real overlaps and sheet overflow.
         */
        _positions.ApplyConfiguredPositions(
            drawingData,
            DrawingViewNames.LayoutOrder);

        _drawingService.Rebuild();

        /*
         * 4. Find the unified Front/Side/Top autoscale.
         *
         * A candidate is accepted only if:
         *  - Front fits the configured height ratio, AND
         *  - Front/Side/Top are inside the sheet, AND
         *  - Front/Side/Top do not overlap each other.
         *
         * Thick wedges (large T/TD/TDF) fail the width/overlap checks
         * at high scales, which pushes the scale down.
         */
        var primaryScale =
            FindPrimaryScale(
                run,
                drawingData,
                profile);

        /*
         * 5. Establish the final unified primary scale.
         */
        _scales.ApplyUnifiedScale(
            DrawingViewNames.Primary,
            primaryScale);

        /*
         * 6. Recalculate every enabled breakline from FINAL scales.
         */
        _breaklines.ApplyEnabled(
            run.WedgeType,
            run.Wedge,
            drawingData,
            profile);

        _drawingService.Rebuild();

        /*
         * 7. Apply configured positions after final scale and breakline
         * geometry are stable.
         *
         * PositionMm normally represents the SolidWorks view origin.
         */
        _positions.ApplyConfiguredPositions(
            drawingData,
            DrawingViewNames.LayoutOrder);

        _drawingService.Rebuild();

        /*
         * CKVD Front and Side are intentionally unbroken in
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

        /*
         * 8. Detail and Section: shrink from their configured scale only
         * as far as needed to stay inside the sheet and clear of the
         * primary views (and of each other). Runs last so it measures the
         * final primary geometry and positions.
         */
        FitSecondaryViews(
            run,
            drawingData,
            profile);

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

        var primaryViews =
            DrawingViewNames.Primary.ToArray();

        var noOthers =
            Array.Empty<string>();

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
             * Front, Side and Top all take the candidate scale because
             * overlap/sheet checks need every primary view measured at
             * the scale being evaluated.
             */
            _scales.ApplyUnifiedScale(
                primaryViews,
                normalized);

            /*
             * Breaklines of enabled primary views are recalculated for
             * every candidate scale, since they change the visible
             * outline that is measured below.
             */
            foreach (var logicalView in primaryViews)
            {
                if (!profile.UsesBreakline(logicalView))
                    continue;

                _breaklines.Apply(
                    logicalView,
                    run.WedgeType,
                    run.Wedge,
                    drawingData);
            }

            /*
             * Scale and breakline mutations must be regenerated before
             * reading the view outlines.
             */
            _drawingService.Rebuild();

            var fitsHeight =
                _geometry.FitsHeight(
                    DrawingViewNames.Front,
                    policy);

            var layoutClean =
                _geometry.IsLayoutClean(
                    primaryViews,
                    noOthers,
                    _fit,
                    out var reason);

            if (fitsHeight && layoutClean)
            {
                Logger.Info(
                    $"[ViewLayout] Autoscale accepted " +
                    $"{normalized:0.###}.");

                return normalized;
            }

            Logger.Info(
                $"[ViewLayout] Autoscale rejected {normalized:0.###}: " +
                (!fitsHeight
                    ? "Front exceeds height fill ratio"
                    : reason) +
                ".");

            candidate -=
                policy.Step;
        }

        Logger.Warn(
            $"[ViewLayout] No scale in range " +
            $"{policy.MinScale:0.###}..{policy.MaxScale:0.###} " +
            "satisfied height and layout constraints. " +
            $"Using MinScale={policy.MinScale:0.###}.");

        return policy.MinScale;
    }

    /// <summary>
    /// Shrinks Detail/Section below their configured scale only when
    /// they leave the sheet or overlap another view.
    /// </summary>
    private void FitSecondaryViews(
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        var primaryViews =
            DrawingViewNames.Primary.ToArray();

        var secondaryViews =
            DrawingViewNames.FixedScale
                .Where(name => _geometry.GetRect(name) is not null)
                .ToArray();

        if (secondaryViews.Length == 0)
            return;

        var configured =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);

        var current =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var logicalView in secondaryViews)
        {
            if (!_scales.TryGetCurrentScale(
                    logicalView,
                    out var scale))
            {
                continue;
            }

            configured[logicalView] = scale;
            current[logicalView] = scale;
        }

        var tracked =
            secondaryViews
                .Where(current.ContainsKey)
                .ToArray();

        /*
         * Phase 1: each secondary view against the sheet and the
         * primary views.
         */
        foreach (var logicalView in tracked)
        {
            var guard = 0;

            while (guard++ < _fit.MaxSecondaryIterations &&
                   !_geometry.IsLayoutClean(
                       new[] { logicalView },
                       primaryViews,
                       _fit,
                       out _))
            {
                if (!StepDownSecondary(
                        logicalView,
                        current,
                        run,
                        drawingData,
                        profile))
                {
                    Logger.Warn(
                        $"[ViewLayout] '{logicalView}' still does not fit " +
                        $"at scale {current[logicalView]:0.###} " +
                        "(minimum reached).");

                    break;
                }
            }
        }

        /*
         * Phase 2: resolve Detail <-> Section overlap by shrinking the
         * view that is furthest above its configured scale.
         */
        if (tracked.Length > 1)
        {
            var guard = 0;

            while (guard++ < _fit.MaxSecondaryIterations &&
                   !_geometry.IsLayoutClean(
                       tracked,
                       Array.Empty<string>(),
                       _fit,
                       out _))
            {
                var candidates =
                    tracked
                        .Where(name =>
                            current[name] - _fit.SecondaryScaleStep >=
                            _fit.SecondaryMinScale - 1e-9)
                        .OrderByDescending(name =>
                            current[name] / configured[name])
                        .ToArray();

                if (candidates.Length == 0)
                {
                    Logger.Warn(
                        "[ViewLayout] Secondary views still overlap " +
                        "at minimum scale.");

                    break;
                }

                StepDownSecondary(
                    candidates[0],
                    current,
                    run,
                    drawingData,
                    profile);
            }
        }

        foreach (var logicalView in tracked)
        {
            if (Math.Abs(current[logicalView] - configured[logicalView]) > 1e-9)
            {
                Logger.Info(
                    $"[ViewLayout] '{logicalView}' reduced from " +
                    $"{configured[logicalView]:0.###} to " +
                    $"{current[logicalView]:0.###} to fit the sheet.");
            }
        }
    }

    private bool StepDownSecondary(
        string logicalView,
        IDictionary<string, double> current,
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        var next =
            current[logicalView] - _fit.SecondaryScaleStep;

        if (next < _fit.SecondaryMinScale - 1e-9)
            return false;

        if (!_scales.ApplyScale(
                logicalView,
                next))
        {
            return false;
        }

        current[logicalView] = next;

        if (profile.UsesBreakline(logicalView))
        {
            _breaklines.Apply(
                logicalView,
                run.WedgeType,
                run.Wedge,
                drawingData);
        }

        _drawingService.Rebuild();

        return true;
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
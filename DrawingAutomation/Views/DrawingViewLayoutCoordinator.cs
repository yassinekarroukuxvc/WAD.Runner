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
/// ViewOriginReader     -> read-only view origins
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
    private readonly ViewOriginReader _origins;
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

        _origins =
            new ViewOriginReader(
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
         * These are treated as MAXIMUM scales (unless the secondary fit
         * policy allows growth); they may be reduced in step 8.
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
         * This is the ONLY place Detail and Section are positioned:
         * step 8 changes their scale only.
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
         * 8. Detail and Section: SCALE ONLY, origins stay where step 7
         * put them.
         *
         * Both must remain completely RIGHT of the rightmost
         * Front/Side/Top outline (including the configured ViewGapMm),
         * even when their Y ranges do not intersect the primary views.
         * They must also stay inside the sheet horizontally and avoid
         * overlapping each other. Vertical overflow remains allowed by
         * default. Scale only; never change their configured origins.
         *
         * The solver first checks whether the current configured scales
         * already obey these rules. Only an actual violation can trigger
         * scale reduction. Every candidate is verified after rebuild.
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

    // ------------------------------------------------------------------
    // Step 8: Detail / Section. NEVER reposition, and NEVER scale up above
    // the configured scale. Front/Side/Top are NEVER modified in this step.
    // ------------------------------------------------------------------

    private const double ScaleEpsilon = 1e-7;
    private const double OriginEpsilonMeters = 1e-6;

    private void FitSecondaryViews(
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        ValidateSecondaryPolicy(_fit);

        var secondaryNames = DrawingViewNames.FixedScale.ToArray();
        var sheet = _geometry.GetUsableSheetRect(_fit.SheetMarginMm)
            ?? throw new InvalidOperationException(
                "[ViewLayout] Cannot fit secondary views: sheet dimensions unavailable.");

        // Primary views are fixed obstacles: we READ them but never scale,
        // reposition, or recalculate their breaklines in Step 8.
        var primaryRects = new List<ViewRect>();
        foreach (var name in DrawingViewNames.Primary)
        {
            var rect = _geometry.GetRect(name);
            if (rect is null)
                throw new InvalidOperationException(
                    $"[ViewLayout] Cannot validate secondary layout: " +
                    $"primary view '{name}' has no measurable outline.");

            primaryRects.Add(rect);
        }

        var configured = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var fixedOrigins = new Dictionary<string, (double X, double Y)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in secondaryNames)
        {
            var rect = _geometry.GetRect(name);
            if (rect is null)
            {
                // Optional views not present in this drawing are ignored.
                // A present view with no outline must NOT be silently ignored.
                if (_scales.TryGetCurrentScale(name, out _))
                    throw new InvalidOperationException(
                        $"[ViewLayout] Secondary view '{name}' has no measurable outline.");

                continue;
            }

            if (!_scales.TryGetCurrentScale(name, out var scale) ||
                !double.IsFinite(scale) || scale <= 0 ||
                !_origins.TryGetOrigin(name, out var x, out var y))
                throw new InvalidOperationException(
                    $"[ViewLayout] Cannot read scale/origin of secondary '{name}'.");

            configured[name] = scale;
            fixedOrigins[name] = (x, y);
        }

        if (configured.Count == 0)
            return;

        var names = configured.Keys.OrderBy(
            n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        if (names.Length > 2)
            throw new InvalidOperationException(
                "[ViewLayout] The secondary solver supports Detail and Section only.");

        var gap = _fit.ViewGapMm / 1000.0;
        var rightBoundary = SecondaryViewFitPlanner.GetRequiredSecondaryMinX(
            sheet, primaryRects, gap);
        Logger.Info(
            $"[ViewLayout] Detail/Section must be entirely right of X=" +
            $"{rightBoundary * 1000.0:0.###} mm " +
            "(rightmost primary outline + gap); origins remain fixed.");

        if (rightBoundary >= sheet.MaxX - 1e-9)
            throw new InvalidOperationException(
                "[ViewLayout] No horizontal space remains to the right " +
                "of the primary views (including the required gap). " +
                "Detail/Section origins and primary views will not be moved.");

        // IMPORTANT: preserve both configured scales when they ALREADY
        // fit the mandatory right-of-primary boundary, horizontal sheet
        // limits, Detail/Section collision clearance, and explicit caps.
        // Neither candidate generation nor fitting repositions any view.
        if (IsActualSecondaryLayoutValid(
                names, configured, fixedOrigins, sheet,
                gap, out _))
        {
            Logger.Info(
                "[ViewLayout] Detail/Section already fit at their configured " +
                "scales: no secondary changes required.");
            return;
        }

        if (_fit.SecondaryFit.AllowGrowAboveConfigured)
            Logger.Warn(
                "[ViewLayout] AllowGrowAboveConfigured is ignored: " +
                "configured secondary scales are hard upper limits.");

        // Fast path: mathematical predictions followed by REAL SolidWorks
        // rebuilds and measurement. Breaklines can make prediction nonlinear.
        if (_fit.SecondaryFit.Enabled)
        {
            for (var pass = 0; pass < Math.Max(1, _fit.SecondaryFit.RefinePasses); pass++)
            {
                var projected = BuildPredictedCandidates(
                    names, configured, fixedOrigins,
                    sheet, primaryRects, gap);

                var ranked = SecondaryViewFitPlanner.RankChoices(
                    projected, configured, gap, _fit.SecondaryFit);

                if (ranked.Count == 0)
                    break;

                if (!ApplySecondaryChoice(
                        ranked[0], names, configured, fixedOrigins,
                        run, drawingData, profile))
                    break; // No changes: another prediction cannot help.

                if (IsActualSecondaryLayoutValid(
                        names, configured, fixedOrigins, sheet,
                        gap, out _))
                {
                    LogSuccessfulSecondaryFit(names, configured);
                    return;
                }

                Logger.Warn(
                    "[ViewLayout] Predicted Detail/Section fit was not valid " +
                    "after SolidWorks rebuild; remeasuring.");
            }
        }

        // Exact fallback: sample each individual secondary at real SolidWorks
        // scales. Once each view's actual rectangles are known, solve the
        // Detail/Section collision using those measured rectangles rather than
        // relying on proportional scaling. No view is repositioned.
        Logger.Info(
            "[ViewLayout] Using measured-geometry Detail/Section search.");

        var measured = new Dictionary<string,
            IReadOnlyList<SecondaryViewFitPlanner.Candidate>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            measured[name] = MeasureSecondaryCandidates(
                name, configured[name], fixedOrigins[name],
                sheet, primaryRects, gap,
                run, drawingData, profile);
        }

        var exactChoices = SecondaryViewFitPlanner.RankChoices(
            measured, configured, gap, _fit.SecondaryFit);

        // Usually the first choice works immediately. Extra attempts protect
        // against SolidWorks modifying derived-view geometry upon a second
        // view's scale change.
        var tries = 0;
        foreach (var choice in exactChoices)
        {
            if (++tries > _fit.MaxSecondaryIterations)
                break;

            ApplySecondaryChoice(
                choice, names, configured, fixedOrigins,
                run, drawingData, profile);

            if (IsActualSecondaryLayoutValid(
                    names, configured, fixedOrigins, sheet,
                    gap, out _))
            {
                LogSuccessfulSecondaryFit(names, configured);
                return;
            }
        }

        // With immovable origins, a feasible scale pair might not exist.
        // Never silently claim that the drawing fits when it does not.
        IsActualSecondaryLayoutValid(
            names, configured, fixedOrigins, sheet,
            gap, out var failureReason);

        throw new InvalidOperationException(
            "[ViewLayout] Detail/Section cannot fit with their static " +
            "positions and configured minimum scales. " +
            $"Last observed issue: {failureReason}. " +
            "Adjust the template positions, scale policy, or allowed margins.");
    }

    private Dictionary<string, IReadOnlyList<SecondaryViewFitPlanner.Candidate>>
        BuildPredictedCandidates(
            IReadOnlyList<string> names,
            IReadOnlyDictionary<string, double> configured,
            IReadOnlyDictionary<string, (double X, double Y)> fixedOrigins,
            ViewRect sheet,
            IReadOnlyList<ViewRect> primaryRects,
            double gap)
    {
        var result = new Dictionary<string,
            IReadOnlyList<SecondaryViewFitPlanner.Candidate>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var currentRect = _geometry.GetRect(name)
                ?? throw new InvalidOperationException(
                    $"[ViewLayout] Missing outline for '{name}'.");

            if (!_scales.TryGetCurrentScale(name, out var currentScale) ||
                !double.IsFinite(currentScale) || currentScale <= 0)
                throw new InvalidOperationException(
                    $"[ViewLayout] Missing runtime scale for '{name}'.");

            var origin = fixedOrigins[name];
            EnsureOriginUnchanged(name, origin);

            var snapshot = new SecondaryViewFitPlanner.Snapshot(
                name, configured[name], currentScale,
                origin.X, origin.Y, currentRect);

            var list = new List<SecondaryViewFitPlanner.Candidate>();
            foreach (var scale in SecondaryViewFitPlanner.ScaleLadder(
                         configured[name], _fit.SecondaryMinScale,
                         _fit.SecondaryScaleStep, _fit.MaxSecondaryIterations))
            {
                var projected = SecondaryViewFitPlanner.Project(snapshot, scale);
                if (SecondaryViewFitPlanner.IsIndividuallyValid(
                        projected, sheet, primaryRects, gap, _fit.SecondaryFit))
                    list.Add(new SecondaryViewFitPlanner.Candidate(scale, projected));
            }

            result[name] = list;
        }

        return result;
    }

    private IReadOnlyList<SecondaryViewFitPlanner.Candidate>
        MeasureSecondaryCandidates(
            string name,
            double configuredScale,
            (double X, double Y) origin,
            ViewRect sheet,
            IReadOnlyList<ViewRect> primaryRects,
            double gap,
            DrawingRun run,
            DrawingData drawingData,
            DrawingProfile profile)
    {
        var measured = new List<SecondaryViewFitPlanner.Candidate>();

        foreach (var candidateScale in SecondaryViewFitPlanner.ScaleLadder(
                     configuredScale, _fit.SecondaryMinScale,
                     _fit.SecondaryScaleStep, _fit.MaxSecondaryIterations))
        {
            if (!_scales.TryGetCurrentScale(name, out var currentScale))
                throw new InvalidOperationException(
                    $"[ViewLayout] Cannot read scale for '{name}'.");

            if (Math.Abs(currentScale - candidateScale) > ScaleEpsilon)
            {
                if (!_scales.ApplyScale(name, candidateScale))
                    throw new InvalidOperationException(
                        $"[ViewLayout] Failed to set '{name}' scale " +
                        $"to {candidateScale:0.###}.");

                if (profile.UsesBreakline(name))
                    _breaklines.Apply(
                        name, run.WedgeType, run.Wedge, drawingData);

                _drawingService.Rebuild();
            }

            EnsureOriginUnchanged(name, origin);

            var rect = _geometry.GetRect(name)
                ?? throw new InvalidOperationException(
                    $"[ViewLayout] Missing rebuilt outline of '{name}'.");

            if (!_scales.TryGetCurrentScale(name, out var actualScale) ||
                Math.Abs(actualScale - candidateScale) > ScaleEpsilon)
                throw new InvalidOperationException(
                    $"[ViewLayout] SolidWorks did not retain independent " +
                    $"scale {candidateScale:0.###} on '{name}' (check parent/sheet scale).");

            if (SecondaryViewFitPlanner.IsIndividuallyValid(
                    rect, sheet, primaryRects, gap, _fit.SecondaryFit))
                measured.Add(new SecondaryViewFitPlanner.Candidate(
                    actualScale, rect));
        }

        return measured;
    }

    /// <summary>
    /// Applies ONLY Detail/Section scale. No position API is called here.
    /// Rebuild and recheck effective scale/origin every time a scale changes.
    /// </summary>
    private bool ApplySecondaryChoice(
        SecondaryViewFitPlanner.Choice choice,
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, double> configured,
        IReadOnlyDictionary<string, (double X, double Y)> fixedOrigins,
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        var changed = false;

        foreach (var name in names)
        {
            if (!choice.ByView.TryGetValue(name, out var target))
                throw new InvalidOperationException(
                    $"[ViewLayout] No scale choice for '{name}'.");

            if (target.Scale > configured[name] + ScaleEpsilon)
                throw new InvalidOperationException(
                    $"[ViewLayout] Refusing to enlarge '{name}' past " +
                    "its configured scale.");

            if (!_scales.TryGetCurrentScale(name, out var current))
                throw new InvalidOperationException(
                    $"[ViewLayout] Cannot read scale for '{name}'.");

            if (Math.Abs(current - target.Scale) < ScaleEpsilon)
                continue;

            if (!_scales.ApplyScale(name, target.Scale))
                throw new InvalidOperationException(
                    $"[ViewLayout] Could not apply scale {target.Scale:0.###} " +
                    $"to '{name}'.");

            if (profile.UsesBreakline(name))
                _breaklines.Apply(name, run.WedgeType, run.Wedge, drawingData);

            changed = true;
        }

        if (changed)
            _drawingService.Rebuild();

        foreach (var name in names)
        {
            EnsureOriginUnchanged(name, fixedOrigins[name]);
            if (!_scales.TryGetCurrentScale(name, out var actual) ||
                Math.Abs(actual - choice.ByView[name].Scale) > ScaleEpsilon)
                throw new InvalidOperationException(
                    $"[ViewLayout] Runtime scale mismatch on '{name}': " +
                    "confirm that its scale is not inherited from a parent/sheet.");
        }

        return changed;
    }

    private bool IsActualSecondaryLayoutValid(
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, double> configured,
        IReadOnlyDictionary<string, (double X, double Y)> fixedOrigins,
        ViewRect sheet,
        double gap,
        out string reason)
    {
        var measured = new Dictionary<string, ViewRect>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            EnsureOriginUnchanged(name, fixedOrigins[name]);

            var rect = _geometry.GetRect(name);
            if (rect is null)
            {
                reason = $"Unable to measure outline of '{name}'";
                return false;
            }

            if (!_scales.TryGetCurrentScale(name, out var scale) ||
                !double.IsFinite(scale) || scale <= 0 ||
                scale > configured[name] + ScaleEpsilon)
            {
                reason = $"Invalid or oversized runtime scale on '{name}'";
                return false;
            }

            measured[name] = rect;
        }

        // Use fresh primary outlines as the final authority. We NEVER
        // change their scale in this step, but SolidWorks can still rebuild
        // dependent geometry when secondary views are regenerated.
        var actualPrimaryRects = new List<ViewRect>();
        foreach (var primaryName in DrawingViewNames.Primary)
        {
            var primaryRect = _geometry.GetRect(primaryName);
            if (primaryRect is null)
            {
                reason = $"Unable to measure primary '{primaryName}'";
                return false;
            }

            actualPrimaryRects.Add(primaryRect);
        }

        return SecondaryViewFitPlanner.TryValidate(
            measured, actualPrimaryRects, sheet, gap,
            _fit.SecondaryFit, out reason);
    }

    private void EnsureOriginUnchanged(
        string name,
        (double X, double Y) expected)
    {
        if (!_origins.TryGetOrigin(name, out var x, out var y) ||
            Math.Abs(x - expected.X) > OriginEpsilonMeters ||
            Math.Abs(y - expected.Y) > OriginEpsilonMeters)
            throw new InvalidOperationException(
                $"[ViewLayout] '{name}' origin moved or could not be read " +
                "during scale fitting. Refusing to reposition it automatically.");
    }

    private void LogSuccessfulSecondaryFit(
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, double> configured)
    {
        foreach (var name in names)
        {
            if (_scales.TryGetCurrentScale(name, out var scale))
                Logger.Info(
                    $"[ViewLayout] '{name}' configured scale=" +
                    $"{configured[name]:0.###}; fitted scale={scale:0.###}; " +
                    "configured origin preserved.");
        }
    }

    private static void ValidateSecondaryPolicy(LayoutFitPolicy fit)
    {
        var policy = fit.SecondaryFit;

        if (!double.IsFinite(fit.SheetMarginMm) || fit.SheetMarginMm < 0 ||
            !double.IsFinite(fit.ViewGapMm) || fit.ViewGapMm < 0 ||
            !double.IsFinite(fit.SecondaryMinScale) || fit.SecondaryMinScale <= 0 ||
            !double.IsFinite(fit.SecondaryScaleStep) || fit.SecondaryScaleStep <= 0 ||
            fit.MaxSecondaryIterations < 2 ||
            !double.IsFinite(policy.MaxWidthFraction) ||
            policy.MaxWidthFraction <= 0 || policy.MaxWidthFraction > 1 ||
            (policy.MaxHeightFraction.HasValue &&
             (!double.IsFinite(policy.MaxHeightFraction.Value) ||
              policy.MaxHeightFraction.Value <= 0)) ||
            !double.IsFinite(policy.DetailPriority) || policy.DetailPriority <= 0 ||
            !double.IsFinite(policy.SectionPriority) || policy.SectionPriority <= 0)
        {
            throw new InvalidOperationException(
                "[ViewLayout] Invalid LayoutFitPolicy / SecondaryFitPolicy.");
        }
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
         * manages as a breakline view. Existing primary behavior unchanged.
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

        if (!double.IsFinite(policy.MinScale) || policy.MinScale <= 0.0)
            throw new InvalidOperationException(
                "ScalePolicy.MinScale must be finite and > 0.");

        if (!double.IsFinite(policy.MaxScale) || policy.MaxScale < policy.MinScale)
            throw new InvalidOperationException(
                "ScalePolicy.MaxScale must be finite and >= MinScale.");

        if (!double.IsFinite(policy.Step) || policy.Step <= 0.0)
            throw new InvalidOperationException(
                "ScalePolicy.Step must be finite and > 0.");

        if (!double.IsFinite(policy.FillRatioHeight) ||
            policy.FillRatioHeight <= 0.0 || policy.FillRatioHeight > 1.0)
            throw new InvalidOperationException(
                "ScalePolicy.FillRatioHeight must be > 0 and <= 1.");

        if (!double.IsFinite(policy.TopMarginMm) ||
            !double.IsFinite(policy.BottomMarginMm) ||
            policy.TopMarginMm < 0.0 || policy.BottomMarginMm < 0.0)
            throw new InvalidOperationException(
                "ScalePolicy margins must be finite and >= 0.");
    }
}

public sealed record ViewLayoutResult(
    double PrimaryScale,
    IReadOnlyDictionary<string, double> FinalScales);

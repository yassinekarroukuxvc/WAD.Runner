using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Wedges;

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
        // drawing_config Metadata "PrimaryLayout" = "FixedScale": Front/Side/Top use their
        // configured scale, so the tool is drawn at the same length on every article (the model
        // TL is fixed); views are moved apart instead of shrunk (step 7b). Otherwise: autoscale.
        var fixedPrimary = UsesFixedPrimaryScale(drawingData);
        var primaryScale = fixedPrimary
            ? ConfiguredPrimaryScale(run, drawingData, profile)
            : FindPrimaryScale(
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

        // 7b. Fixed primary scale: a thicker tool is wider at the same scale, so Side (and Top,
        // which is projected from it) move right until they clear Front.
        if (fixedPrimary)
            SpreadPrimaryViews(run, drawingData, primaryScale);

        /*
         * 8. Detail and Section: SCALE ONLY, origins stay where step 7
         * put them.
         *
         * Horizontally they must stay inside the sheet and clear of the
         * primary views. Vertically they may extend past the sheet
         * (unless SecondaryFitPolicy.ClampToSheetHeight is set); they
         * still may not overlap each other or the primary views.
         *
         * The largest scale that fits is solved in closed form from the
         * origin-to-edge distances (scaling is about the origin). If the
         * two views would still collide, both are shrunk by the same
         * factor. The step-down safety net then only acts if something is
         * still not clean (for example because breaklines made the
         * outline non-linear in scale).
         */
        if (UsesPackedSecondaryLayout(drawingData))
        {
            // Packing updates drawingData origins for later annotation placement. Keep the
            // originals for a reliable fallback if a SolidWorks rebuild/fit step throws.
            var originalSecondaryPositions = DrawingViewNames.FixedScale
                .Where(name => drawingData.Views.ContainsKey(name))
                .ToDictionary(name => name, name => drawingData.Views[name].PositionMm.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            try
            {
                PackSecondaryViews(run, drawingData, profile);
            }
            catch (Exception ex)
            {
                // Restore the original config origins, not positions already changed by packing.
                foreach (var item in originalSecondaryPositions)
                    drawingData.Views[item.Key].PositionMm = item.Value;
                Logger.Warn($"[ViewLayout/Packed] {ex.Message} Falling back to the legacy Detail/Section fit.");
                _scales.ApplyConfiguredScales(drawingData, DrawingViewNames.FixedScale);
                _breaklines.ApplyEnabled(run.WedgeType, run.Wedge, drawingData, profile);
                _positions.ApplyConfiguredPositions(drawingData, DrawingViewNames.FixedScale);
                _drawingService.Rebuild();
                FitSecondaryViews(run, drawingData, profile);
            }
        }
        else
        {
            FitSecondaryViews(
                run,
                drawingData,
                profile);
        }

        // A module may couple break heights across views. Reapply at the final pair
        // of scales rather than retaining a height calculated while its sibling was fitting.
        if (DrawingWedgeModuleRegistry.Get(run.WedgeType).Behavior.RefineBreakline is not null)
        {
            _breaklines.ApplyEnabled(run.WedgeType, run.Wedge, drawingData, profile);
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
            finalScales,
            new Dictionary<string, double>(_breaklines.RetainedHeightsMm));
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

    private static bool UsesFixedPrimaryScale(DrawingData drawingData)
        => drawingData.Metadata.TryGetValue("PrimaryLayout", out var mode) &&
           string.Equals(mode?.Trim(), "FixedScale", StringComparison.OrdinalIgnoreCase);

    private static double ConfiguredPrimaryScale(DrawingRun run, DrawingData drawingData, DrawingProfile profile)
    {
        var configured = drawingData.Views.TryGetValue(DrawingViewNames.Front, out var front) && front.Scale > 0
            ? front.Scale
            : profile.Scale.MaxScale;

        // A longer display TL (thick tool) is drawn at a proportionally smaller scale, so the tool
        // keeps the same drawn length and Front/Side/Top keep about the same width.
        var behavior = DrawingWedgeModuleRegistry.Get(run.WedgeType).Behavior;
        var factor = 1.0;
        if (behavior.ResolveBreaklineTlMm is { } resolveTl && behavior.BreaklineTlOverrideMm is { } baseTl)
        {
            var tl = resolveTl(run.Wedge);
            if (tl > 0 && baseTl > 0)
                factor = (double)baseTl / tl;
        }

        // A reduced scale is rounded down to 0.1 so the title-block scale text stays short.
        var scale = factor < 1.0 ? Math.Floor(configured * factor * 10.0) / 10.0 : configured;
        Logger.Info(
            $"[ViewLayout] Fixed primary scale {scale:0.###} (configured {configured:0.###}" +
            (factor < 1.0 ? $" x {factor:0.###} for the longer display TL" : string.Empty) + ").");
        return scale;
    }

    /// <summary>
    /// Fixed primary scale: a thicker tool is wider at the same scale. Front keeps room on its left
    /// for the TL dimension, Side (and Top, projected from it) move right until they clear Front, and
    /// Top moves down if its top (with the TDF dimension above it) would reach the header boxes.
    /// Parts that already fit are not moved.
    /// </summary>
    private void SpreadPrimaryViews(DrawingRun run, DrawingData drawingData, double scale)
    {
        void Move(string name, double dx, double dy)
        {
            if (_positions.MoveBy(name, dx, dy) is { } moved && drawingData.Views.TryGetValue(name, out var cfg))
                cfg.PositionMm = new[] { moved[0] * 1000.0, moved[1] * 1000.0 };
        }

        // Drawn geometry, not GetOutline (wider: breaks, centre-mark lines).
        ViewRect? Drawn(string name)
        {
            if (_geometry.GetVisibleGeometryRect(name) is not { } v ||
                !_origins.TryGetOrigin(name, out var ox, out var oy))
                return _geometry.GetRect(name);
            return new ViewRect(ox + v.MinX * scale, oy + v.MinY * scale, ox + v.MaxX * scale, oy + v.MaxY * scale);
        }

        var moves = new List<string>();

        // A display TL longer than the base length makes the template's shank grow upward from an
        // origin that stays near the foot; move Front and Side down by the extra drawn length so the
        // tool top stays where it is on a normal part (the dimension rules use the same offset).
        var behavior = DrawingWedgeModuleRegistry.Get(run.WedgeType).Behavior;
        if (behavior.ResolveBreaklineTlMm is { } resolveTl && behavior.BreaklineTlOverrideMm is { } baseTl)
        {
            var extra = (resolveTl(run.Wedge) - (double)baseTl) * scale / 1000.0;
            if (extra > 1e-6)
            {
                Move(DrawingViewNames.Front, 0.0, -extra);
                Move(DrawingViewNames.Side, 0.0, -extra);
                _drawingService.Rebuild();
                moves.Add($"Front/Side {extra * 1000:0.##} mm down (display TL)");
            }
        }

        if (Drawn(DrawingViewNames.Front) is not { } front)
            return;

        var frontDx = _fit.FrontMinLeftMm / 1000.0 - front.MinX;
        if (frontDx > 1e-6)
        {
            Move(DrawingViewNames.Front, frontDx, 0.0);
            front = new ViewRect(front.MinX + frontDx, front.MinY, front.MaxX + frontDx, front.MaxY);
            moves.Add($"Front {frontDx * 1000:0.##} mm right");
        }

        if (Drawn(DrawingViewNames.Side) is { } side)
        {
            var sideDx = front.MaxX + _fit.MinPrimaryGapMm / 1000.0 - side.MinX;
            if (sideDx > 1e-6)
            {
                Move(DrawingViewNames.Side, sideDx, 0.0);
                Move(DrawingViewNames.Top, sideDx, 0.0);
                moves.Add($"Side/Top {sideDx * 1000:0.##} mm right");
            }
        }

        // Top is not broken, so its drawn Y extent is exact.
        if (Drawn(DrawingViewNames.Top) is { } top)
        {
            var topDy = _fit.TopMaxTopMm / 1000.0 - top.MaxY;
            if (topDy < -1e-6)
            {
                Move(DrawingViewNames.Top, 0.0, topDy);
                moves.Add($"Top {-topDy * 1000:0.##} mm down");
            }
        }

        if (moves.Count == 0)
        {
            Logger.Info("[ViewLayout] Fixed primary scale: Front/Side/Top already clear, not moved.");
            return;
        }

        _drawingService.Rebuild();
        Logger.Info($"[ViewLayout] Fixed primary scale: moved {string.Join(", ", moves)}.");
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
    // Step 8 (packed): Detail / Section share one scale, as large as fits the
    // free region right of the primary views, placed side by side and centred.
    // drawing_config Metadata "SecondaryLayout" = "Legacy" selects the method below.
    // ------------------------------------------------------------------

    private static bool UsesPackedSecondaryLayout(DrawingData drawingData)
        => !(drawingData.Metadata.TryGetValue("SecondaryLayout", out var mode) &&
             string.Equals(mode?.Trim(), "Legacy", StringComparison.OrdinalIgnoreCase));

    private void PackSecondaryViews(
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        var pack = _fit.PackedLayout;
        var names = DrawingViewNames.FixedScale
            .Where(n => _geometry.GetRect(n) is not null && _scales.TryGetCurrentScale(n, out _))
            .OrderBy(n => drawingData.Views.TryGetValue(n, out var v) ? v.PositionMm[0] : 0.0)
            .ToArray();
        if (names.Length == 0)
            return;

        var sheet = _geometry.GetUsableSheetRect(_fit.SheetMarginMm)
            ?? throw new InvalidOperationException("Sheet dimensions unavailable.");

        var primaryRight = DrawingViewNames.Primary
            .Select(n => _geometry.GetRect(n))
            .Where(r => r is not null)
            .Select(r => r!.MaxX)
            .DefaultIfEmpty(sheet.MinX)
            .Max();

        var moduleRightMm = DrawingWedgeModuleRegistry.Get(run.WedgeType).Behavior
            .ResolveSecondaryRightMarginMm?.Invoke(run.Wedge, drawingData) ?? 0.0;

        var left = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var right = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var isSection = string.Equals(name, DrawingViewNames.Section, StringComparison.OrdinalIgnoreCase);
            left[name] = (isSection ? pack.SectionLeftMm : pack.DetailLeftMm) / 1000.0;
            right[name] = (isSection ? pack.SectionRightMm : pack.DetailRightMm) / 1000.0;
        }
        var last = names[names.Length - 1];
        right[last] = Math.Max(right[last], moduleRightMm / 1000.0);

        var gap = pack.ViewGapMm / 1000.0;
        var regionMinX = primaryRight + pack.PrimaryRightAllowanceMm / 1000.0;
        var regionWidth = sheet.MaxX - regionMinX;
        var reserved = names.Sum(n => left[n] + right[n]) + gap * (names.Length - 1);
        if (regionWidth <= reserved)
            throw new InvalidOperationException(
                $"No room right of the primary views ({regionWidth * 1000:0.#} mm for {reserved * 1000:0.#} mm of annotations).");

        // Configured scales stay the upper limit.
        var maxScale = names.Min(n => drawingData.Views.TryGetValue(n, out var v) && v.Scale > 0 ? v.Scale : 100.0);
        var minScale = _fit.SecondaryMinScale;

        // Width on paper is linear in scale (the kept height below the break is fixed on paper):
        // w(s) = a*s + b. Two measurements give a and b; TD (the tool diameter, the widest part)
        // bounds a when a measurement is unusable.
        var s1 = maxScale;
        var s2 = Math.Max(minScale, maxScale * pack.CalibrationFraction);
        var w1 = ApplySecondaryScaleAndMeasure(names, s1, run, drawingData, profile);
        var w2 = ApplySecondaryScaleAndMeasure(names, s2, run, drawingData, profile);

        var td = run.Wedge.TryGet(WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey.From("TD"));
        var tdMeters = td is not null && td.Nominal.Unit == WAD.Runner.DataManagement.Domain.Units.UnitKind.Millimeter
            ? (double)td.Nominal.Value / 1000.0
            : 0.0;

        double sumA = 0, sumB = 0;
        foreach (var name in names)
        {
            var a = (w1[name] - w2[name]) / (s1 - s2);
            var b = w1[name] - a * s1;
            if (!(a > 0) || !double.IsFinite(b))
            {
                if (tdMeters <= 0)
                    throw new InvalidOperationException($"Could not calibrate the width of '{name}'.");
                a = tdMeters;
                b = 0.0;
            }

            Logger.Info($"[ViewLayout/Packed] '{name}' width(scale) = {a * 1000:0.####} mm x scale + {b * 1000:0.##} mm.");
            sumA += a;
            sumB += b;
        }

        var scale = Math.Clamp((regionWidth - reserved - sumB) / sumA, minScale, maxScale);
        var target = regionWidth - reserved;

        // The line through the two calibration points is only an estimate: a module may keep more
        // height below the break at larger scales (4516 funnel), so the width can be non-linear or
        // jump. Each try is measured; the search keeps the largest scale that fits (lo) and the
        // smallest that overflows (hi), tries the secant estimate when it lies between them and
        // the midpoint otherwise, and stops when the region is filled to within 2 % or the bracket
        // is narrower than 2 %.
        const double FillTolerance = 0.02;
        const int MaxSearchSteps = 12;
        (double Scale, double Width)? lo = null, hi = null;
        var prev = (Scale: s1, Width: names.Sum(n => w1[n]));
        Dictionary<string, double> widths = new(StringComparer.OrdinalIgnoreCase);
        double total;

        for (var step = 1; ; step++)
        {
            try
            {
                widths = ApplySecondaryScaleAndMeasure(names, scale, run, drawingData, profile);
            }
            catch (InvalidOperationException ex) when (step < MaxSearchSteps && scale < maxScale - ScaleEpsilon)
            {
                // A module breakline can be impossible at a small scale (4516: the fixed display
                // length is no longer than the kept height). That scale is too small: raise the floor.
                minScale = Math.Min(maxScale, scale * 1.25);
                Logger.Info($"[ViewLayout/Packed] Scale {scale:0.###} rejected ({ex.Message}); trying {minScale:0.###}.");
                scale = Math.Max(minScale, lo?.Scale ?? minScale);
                continue;
            }

            var sumWidth = names.Sum(n => widths[n]);
            total = reserved + sumWidth;
            var fits = total <= regionWidth + 1e-6;

            if (fits && (lo is null || scale > lo.Value.Scale)) lo = (scale, sumWidth);
            if (!fits && (hi is null || scale < hi.Value.Scale)) hi = (scale, sumWidth);

            var filled = fits && (regionWidth - total <= FillTolerance * regionWidth || scale >= maxScale - ScaleEpsilon);
            var bracketTight = lo is not null && hi is not null &&
                               hi.Value.Scale - lo.Value.Scale <= FillTolerance * lo.Value.Scale;
            if (filled || bracketTight || step >= MaxSearchSteps)
                break;

            var slope = Math.Abs(scale - prev.Scale) > ScaleEpsilon ? (sumWidth - prev.Width) / (scale - prev.Scale) : 0.0;
            var next = slope > 0 ? scale + (target - sumWidth) / slope : scale * (fits ? 1.1 : 0.9);
            var low = lo?.Scale ?? minScale;
            var high = hi?.Scale ?? maxScale;
            if (!(next > low + ScaleEpsilon && next < high - ScaleEpsilon))
                next = hi is null ? Math.Min(maxScale, Math.Max(next, scale * 1.1)) : (low + high) / 2.0;

            Logger.Info(
                $"[ViewLayout/Packed] Scale {scale:0.###} {(fits ? "leaves room" : "overflows")} " +
                $"(used {total * 1000:0.#} of {regionWidth * 1000:0.#} mm); next {next:0.###}.");
            prev = (scale, sumWidth);
            scale = Math.Clamp(next, minScale, maxScale);
        }

        if (lo is null)
            throw new InvalidOperationException($"No common scale down to {minScale:0.###} fits the free region.");

        if (Math.Abs(scale - lo.Value.Scale) > ScaleEpsilon)
        {
            scale = lo.Value.Scale;
            widths = ApplySecondaryScaleAndMeasure(names, scale, run, drawingData, profile);
            total = reserved + names.Sum(n => widths[n]);
        }

        for (var attempt = 1; attempt <= Math.Max(1, pack.MaxAttempts); attempt++)
        {
            if (attempt > 1)
            {
                widths = ApplySecondaryScaleAndMeasure(names, scale, run, drawingData, profile);
                total = reserved + names.Sum(n => widths[n]);
            }

            var x = regionMinX + Math.Max(0.0, regionWidth - total) / 2.0;

            foreach (var name in names)
            {
                var span = GetVisibleSpanX(name, scale);
                var dx = x + left[name] - span.MinX;
                var moved = _positions.MoveBy(name, dx, 0.0)
                    ?? throw new InvalidOperationException($"Could not move '{name}'.");
                // The annotation planner places callouts relative to this configured origin.
                if (drawingData.Views.TryGetValue(name, out var cfg))
                    cfg.PositionMm = new[] { moved[0] * 1000.0, moved[1] * 1000.0 };
                x += left[name] + widths[name] + right[name] + gap;
            }

            _drawingService.Rebuild();

            if (PackedLayoutIsClean(names, scale, left, right, regionMinX, sheet, gap, out var reason))
            {
                foreach (var name in names)
                    Logger.Info(
                        $"[ViewLayout/Packed] '{name}' scale={scale:0.###}, " +
                        $"origin=({drawingData.Views[name].PositionMm[0]:0.##}, {drawingData.Views[name].PositionMm[1]:0.##}) mm, " +
                        $"width={widths[name] * 1000:0.#} mm.");
                Logger.Success(
                    $"[ViewLayout/Packed] Detail/Section packed at common scale {scale:0.###} " +
                    $"(region {regionWidth * 1000:0.#} mm, used {total * 1000:0.#} mm).");
                return;
            }

            Logger.Warn($"[ViewLayout/Packed] Attempt {attempt} at scale {scale:0.###} not clean ({reason}); shrinking.");
            scale = Math.Max(minScale, scale * pack.RetryShrinkFactor);
        }

        throw new InvalidOperationException("Packed layout did not fit after the allowed attempts.");
    }

    private Dictionary<string, double> ApplySecondaryScaleAndMeasure(
        IReadOnlyList<string> names,
        double scale,
        DrawingRun run,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        foreach (var name in names)
        {
            if (!_scales.ApplyScale(name, scale))
                throw new InvalidOperationException($"Could not set '{name}' scale to {scale:0.###}.");
            if (profile.UsesBreakline(name))
                _breaklines.Apply(name, run.WedgeType, run.Wedge, drawingData);
        }

        _drawingService.Rebuild();

        var widths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var span = GetVisibleSpanX(name, scale);
            widths[name] = span.MaxX - span.MinX;
        }
        return widths;
    }

    /// <summary>
    /// Horizontal sheet extent (meters) of what the view draws. GetOutline of a broken view spans
    /// the unbroken geometry (the full TD width), so the displayed edges are used: they are in
    /// unscaled view coordinates about the view position, and breaks only shift them vertically.
    /// Falls back to the outline when the edges cannot be read.
    /// </summary>
    private (double MinX, double MaxX) GetVisibleSpanX(string name, double scale)
    {
        var outline = _geometry.GetRect(name)
            ?? throw new InvalidOperationException($"Missing outline of '{name}'.");

        if (_geometry.GetVisibleGeometryRect(name) is { } visible &&
            _origins.TryGetOrigin(name, out var originX, out _))
        {
            var minX = originX + visible.MinX * scale;
            var maxX = originX + visible.MaxX * scale;
            if (maxX > minX && minX >= outline.MinX - 1e-4 && maxX <= outline.MaxX + 1e-4)
                return (minX, maxX);

            Logger.Warn($"[ViewLayout/Packed] '{name}' visible edges ({minX * 1000:0.#}..{maxX * 1000:0.#} mm) " +
                        "outside its outline; using the outline.");
        }

        return (outline.MinX, outline.MaxX);
    }

    private bool PackedLayoutIsClean(
        IReadOnlyList<string> names,
        double scale,
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right,
        double regionMinX,
        ViewRect sheet,
        double gap,
        out string reason)
    {
        var rects = new List<(string Name, ViewRect Rect)>();
        foreach (var name in names)
        {
            var span = GetVisibleSpanX(name, scale);

            // Annotation areas included: they must stay inside the region and apart.
            var padded = new ViewRect(span.MinX - left[name], 0.0, span.MaxX + right[name], 1.0);
            if (padded.MinX < regionMinX - 1e-6 || padded.MaxX > sheet.MaxX + 1e-6)
            {
                reason = $"'{name}' leaves the free region";
                return false;
            }

            rects.Add((name, padded));
        }

        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
        {
            if (rects[i].Rect.MaxX + gap - 1e-6 > rects[j].Rect.MinX &&
                rects[j].Rect.MaxX + gap - 1e-6 > rects[i].Rect.MinX)
            {
                reason = $"'{rects[i].Name}' and '{rects[j].Name}' overlap";
                return false;
            }
        }

        reason = string.Empty;
        return true;
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
        var annotationMargin = (DrawingWedgeModuleRegistry.Get(run.WedgeType).Behavior
            .ResolveSecondaryRightMarginMm?.Invoke(run.Wedge, drawingData) ?? 0.0) / 1000.0;
        sheet = new ViewRect(sheet.MinX, sheet.MinY, sheet.MaxX - annotationMargin, sheet.MaxY);

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

        // IMPORTANT: a drawing that already fits is left EXACTLY as-is.
        // No artificial right-of-primary region, no scale rounding, no moves.
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
    IReadOnlyDictionary<string, double> FinalScales,
    IReadOnlyDictionary<string, double> RetainedHeightsMm);

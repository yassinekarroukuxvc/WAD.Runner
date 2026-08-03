using System;
using System.Collections.Generic;
using System.Globalization;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Interop;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Owns drawing-view movement only.
///
/// This service never:
/// - changes a view scale
/// - applies breaklines
/// - rebuilds the model
///
/// Rebuild timing belongs to the layout coordinator.
/// </summary>
public sealed class ViewPositionService
{
    private const double PositionToleranceMeters = 1e-9;

    private readonly DrawingDoc _drawing;
    private readonly IDictionary<string, string> _logicalToActual;

    public ViewPositionService(
        DrawingService drawingService,
        IDictionary<string, string>? logicalToActual = null)
    {
        if (drawingService is null)
            throw new ArgumentNullException(nameof(drawingService));

        _drawing =
            drawingService.Drawing
            ?? throw new InvalidOperationException(
                "No active drawing.");

        _logicalToActual =
            logicalToActual
            ?? new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Breaks alignment and unlocks the supplied views once, before the
    /// layout workflow starts moving them.
    ///
    /// This is intentionally separate from ApplyConfiguredPosition so
    /// changing a position does not repeatedly alter SolidWorks view
    /// relationships.
    /// </summary>
    public void PrepareForMovement(
        IEnumerable<string> logicalViews)
    {
        if (logicalViews is null)
            throw new ArgumentNullException(nameof(logicalViews));

        foreach (var logicalView in logicalViews)
        {
            if (string.IsNullOrWhiteSpace(logicalView))
                continue;

            var view =
                FindView(
                    logicalView);

            if (view is null)
            {
                Logger.Warn(
                    $"[ViewPosition] View '{logicalView}' was not found " +
                    "while preparing movement.");

                continue;
            }

            try
            {
                InteropCompat.TryBreakAlignment(
                    view);

                InteropCompat.TryUnlock(
                    view);
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[ViewPosition] Failed to prepare '{logicalView}' " +
                    $"for movement: {ex.Message}");
            }
        }
    }

    public bool ApplyConfiguredPosition(
        string logicalView,
        DrawingData drawingData)
    {
        if (!TryGetConfiguredPositionMm(
                logicalView,
                drawingData,
                out var xMm,
                out var yMm))
        {
            return false;
        }

        var view =
            FindView(
                logicalView);

        if (view is null)
        {
            Logger.Warn(
                $"[ViewPosition] View '{logicalView}' was not found.");

            return false;
        }

        try
        {
            var xMeters =
                xMm / 1000.0;

            var yMeters =
                yMm / 1000.0;

            /*
             * Do not silently clamp the configured point to the sheet.
             *
             * Clamping only the view origin does not guarantee that the
             * complete view outline is inside the sheet and can hide bad
             * layout configuration.
             */
            view.Position =
                new[]
                {
                    xMeters,
                    yMeters
                };

            Logger.Info(
                $"[ViewPosition] '{logicalView}' positioned at " +
                $"({xMm.ToString("0.###", CultureInfo.InvariantCulture)} mm, " +
                $"{yMm.ToString("0.###", CultureInfo.InvariantCulture)} mm).");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ViewPosition] Failed to position '{logicalView}': " +
                ex.Message);

            return false;
        }
    }

    public void ApplyConfiguredPositions(
        DrawingData drawingData,
        IEnumerable<string> logicalViews)
    {
        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        if (logicalViews is null)
            throw new ArgumentNullException(nameof(logicalViews));

        foreach (var logicalView in logicalViews)
        {
            if (string.IsNullOrWhiteSpace(logicalView))
                continue;

            ApplyConfiguredPosition(
                logicalView,
                drawingData);
        }
    }

    /// <summary>
    /// Moves only the SolidWorks view-origin Y coordinate so the center of
    /// the final visible view outline lands on the configured PositionMm Y.
    ///
    /// The current X origin is preserved exactly. This is intended for
    /// unbroken CKVD Front/Side views whose model origin is vertically
    /// offset from the center of their visible geometry.
    ///
    /// The coordinator must call this only after final scales and breakline
    /// states are established and the drawing has been rebuilt.
    /// </summary>
    public bool AlignVisibleCenterYToConfiguredPosition(
        string logicalView,
        DrawingData drawingData)
    {
        if (!TryGetConfiguredPositionMm(
                logicalView,
                drawingData,
                out _,
                out var targetCenterYMm))
        {
            return false;
        }

        var view =
            FindView(
                logicalView);

        if (view is null)
        {
            Logger.Warn(
                $"[ViewPosition] View '{logicalView}' was not found " +
                "while applying visible-center Y positioning.");

            return false;
        }

        if (!TryReadViewPosition(
                view,
                out var currentOriginX,
                out var currentOriginY))
        {
            Logger.Warn(
                $"[ViewPosition] Could not read the current origin of " +
                $"'{logicalView}'. Visible-center Y positioning was skipped.");

            return false;
        }

        if (!InteropCompat.TryGetViewOutline(
                view,
                out _,
                out var minY,
                out _,
                out var maxY))
        {
            Logger.Warn(
                $"[ViewPosition] Could not read the outline of " +
                $"'{logicalView}'. Visible-center Y positioning was skipped.");

            return false;
        }

        var currentOutlineCenterY =
            (minY + maxY) * 0.5;

        var targetOutlineCenterY =
            targetCenterYMm / 1000.0;

        var deltaY =
            targetOutlineCenterY - currentOutlineCenterY;

        if (!double.IsFinite(currentOutlineCenterY) ||
            !double.IsFinite(targetOutlineCenterY) ||
            !double.IsFinite(deltaY))
        {
            Logger.Warn(
                $"[ViewPosition] Non-finite visible-center calculation " +
                $"for '{logicalView}'. Position was not changed.");

            return false;
        }

        if (Math.Abs(deltaY) <= PositionToleranceMeters)
        {
            Logger.Info(
                $"[ViewPosition] '{logicalView}' visible center is already " +
                $"at configured Y={targetCenterYMm.ToString("0.###", CultureInfo.InvariantCulture)} mm.");

            return true;
        }

        var correctedOriginY =
            currentOriginY + deltaY;

        if (!double.IsFinite(correctedOriginY))
        {
            Logger.Warn(
                $"[ViewPosition] Corrected origin Y for '{logicalView}' " +
                "is not finite. Position was not changed.");

            return false;
        }

        try
        {
            view.Position =
                new[]
                {
                    currentOriginX,
                    correctedOriginY
                };

            Logger.Info(
                $"[ViewPosition] '{logicalView}' visible-center Y corrected. " +
                $"TargetCenterY={targetCenterYMm.ToString("0.###", CultureInfo.InvariantCulture)} mm, " +
                $"BeforeCenterY={(currentOutlineCenterY * 1000.0).ToString("0.###", CultureInfo.InvariantCulture)} mm, " +
                $"DeltaY={(deltaY * 1000.0).ToString("0.###", CultureInfo.InvariantCulture)} mm, " +
                $"NewOriginY={(correctedOriginY * 1000.0).ToString("0.###", CultureInfo.InvariantCulture)} mm.");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ViewPosition] Failed to apply visible-center Y " +
                $"positioning to '{logicalView}': {ex.Message}");

            return false;
        }
    }

    private bool TryGetConfiguredPositionMm(
        string logicalView,
        DrawingData drawingData,
        out double xMm,
        out double yMm)
    {
        xMm = 0.0;
        yMm = 0.0;

        if (string.IsNullOrWhiteSpace(logicalView))
            return false;

        if (drawingData?.Views is null)
            return false;

        if (!drawingData.Views.TryGetValue(
                logicalView,
                out var viewConfig)
            || viewConfig is null)
        {
            Logger.Warn(
                $"[ViewPosition] No configuration found for " +
                $"'{logicalView}'; position was not changed.");

            return false;
        }

        var position =
            viewConfig.PositionMm;

        if (position is null ||
            position.Length < 2)
        {
            Logger.Warn(
                $"[ViewPosition] '{logicalView}' has no valid " +
                "PositionMm[2] configuration.");

            return false;
        }

        xMm =
            position[0];

        yMm =
            position[1];

        if (!double.IsFinite(xMm) ||
            !double.IsFinite(yMm))
        {
            Logger.Warn(
                $"[ViewPosition] '{logicalView}' has a non-finite " +
                $"position ({xMm}, {yMm}).");

            return false;
        }

        return true;
    }

    private static bool TryReadViewPosition(
        View view,
        out double x,
        out double y)
    {
        x = 0.0;
        y = 0.0;

        if (view is null)
            return false;

        try
        {
            var positionObject =
                view.Position;

            if (positionObject is double[] doubles &&
                doubles.Length >= 2)
            {
                x = doubles[0];
                y = doubles[1];
                return double.IsFinite(x) && double.IsFinite(y);
            }

            if (positionObject is object[] objects &&
                objects.Length >= 2)
            {
                x = Convert.ToDouble(
                    objects[0],
                    CultureInfo.InvariantCulture);

                y = Convert.ToDouble(
                    objects[1],
                    CultureInfo.InvariantCulture);

                return double.IsFinite(x) && double.IsFinite(y);
            }

            if (positionObject is Array array &&
                array.Length >= 2)
            {
                x = Convert.ToDouble(
                    array.GetValue(0),
                    CultureInfo.InvariantCulture);

                y = Convert.ToDouble(
                    array.GetValue(1),
                    CultureInfo.InvariantCulture);

                return double.IsFinite(x) && double.IsFinite(y);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private View? FindView(
        string logicalView)
    {
        var actualName =
            ResolveActualName(
                logicalView);

        return ViewFinder.FindByName(
            _drawing,
            actualName);
    }

    private string ResolveActualName(
        string logicalView)
    {
        return
            _logicalToActual.TryGetValue(
                logicalView,
                out var mapped)
            && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : logicalView;
    }
}
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

        var xMm =
            position[0];

        var yMm =
            position[1];

        if (!double.IsFinite(xMm) ||
            !double.IsFinite(yMm))
        {
            Logger.Warn(
                $"[ViewPosition] '{logicalView}' has a non-finite " +
                $"position ({xMm}, {yMm}).");

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

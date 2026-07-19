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
/// Owns drawing-view scale changes only.
///
/// This service never:
/// - moves views
/// - applies breaklines
/// - rebuilds the model
/// - mutates DrawingData
///
/// Rebuild timing belongs to the layout coordinator.
/// </summary>
public sealed class ViewScaleService
{
    private const double MinimumValidScale = 1e-9;

    private readonly DrawingDoc _drawing;
    private readonly IDictionary<string, string> _logicalToActual;

    public ViewScaleService(
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

    public bool ApplyConfiguredScale(
        DrawingData drawingData,
        string logicalView)
    {
        if (drawingData?.Views is null)
            return false;

        if (string.IsNullOrWhiteSpace(logicalView))
            return false;

        if (!drawingData.Views.TryGetValue(
                logicalView,
                out var viewConfig)
            || viewConfig is null)
        {
            Logger.Warn(
                $"[ViewScale] No configuration found for " +
                $"'{logicalView}'; scale was not changed.");

            return false;
        }

        return ApplyScale(
            logicalView,
            viewConfig.Scale);
    }

    public void ApplyConfiguredScales(
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

            ApplyConfiguredScale(
                drawingData,
                logicalView);
        }
    }

    public bool ApplyScale(
        string logicalView,
        double scale)
    {
        if (string.IsNullOrWhiteSpace(logicalView))
            return false;

        if (!IsValidScale(scale))
        {
            Logger.Warn(
                $"[ViewScale] Invalid scale '{scale}' for " +
                $"'{logicalView}'; scale was not changed.");

            return false;
        }

        var view =
            FindView(
                logicalView);

        if (view is null)
        {
            Logger.Warn(
                $"[ViewScale] View '{logicalView}' was not found.");

            return false;
        }

        try
        {
            InteropCompat.TrySetScale(
                view,
                scale);

            Logger.Info(
                $"[ViewScale] '{logicalView}' scale = " +
                scale.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ViewScale] Failed to set scale for " +
                $"'{logicalView}': {ex.Message}");

            return false;
        }
    }

    public void ApplyUnifiedScale(
        IEnumerable<string> logicalViews,
        double scale)
    {
        if (logicalViews is null)
            throw new ArgumentNullException(nameof(logicalViews));

        if (!IsValidScale(scale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                scale,
                "Scale must be finite and greater than zero.");
        }

        foreach (var logicalView in logicalViews)
        {
            if (string.IsNullOrWhiteSpace(logicalView))
                continue;

            ApplyScale(
                logicalView,
                scale);
        }
    }

    public bool TryGetCurrentScale(
        string logicalView,
        out double scale)
    {
        scale = 0.0;

        if (string.IsNullOrWhiteSpace(logicalView))
            return false;

        var view =
            FindView(
                logicalView);

        if (view is null)
            return false;

        try
        {
            var current =
                InteropCompat.GetScaleDecimalOr(
                    view,
                    0.0);

            if (!IsValidScale(current))
                return false;

            scale =
                current;

            return true;
        }
        catch
        {
            return false;
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

    private static bool IsValidScale(
        double scale)
    {
        return
            double.IsFinite(scale)
            && scale > MinimumValidScale;
    }
}

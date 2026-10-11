using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Interop;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Read-only: returns a view's current origin in sheet coordinates
/// (meters). Never moves a view.
/// </summary>
public sealed class ViewOriginReader
{
    private readonly DrawingDoc _drawing;
    private readonly IDictionary<string, string> _logicalToActual;

    public ViewOriginReader(
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

    public bool TryGetOrigin(
        string logicalView,
        out double x,
        out double y)
    {
        x = 0.0;
        y = 0.0;

        var actualName =
            _logicalToActual.TryGetValue(
                logicalView,
                out var mapped)
            && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : logicalView;

        var view =
            ViewFinder.FindByName(
                _drawing,
                actualName);

        if (view is null)
            return false;

        try
        {
            if (view.Position is not double[] position ||
                position.Length < 2 ||
                !double.IsFinite(position[0]) ||
                !double.IsFinite(position[1]))
            {
                return false;
            }

            x = position[0];
            y = position[1];

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ViewOrigin] Failed to read origin of " +
                $"'{logicalView}': {ex.Message}");

            return false;
        }
    }
}
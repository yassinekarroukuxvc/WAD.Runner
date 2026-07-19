using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Drawing-level breakline orchestration.
///
/// This service:
/// - resolves logical view names
/// - checks profile enablement
/// - creates a per-view BreaklineHandler
///
/// It does not rebuild. The layout coordinator owns rebuild boundaries.
/// </summary>
public sealed class BreaklineService
{
    private static readonly string[] SupportedLogicalViews =
    {
        "Front",
        "Side",
        "Detail",
        "Section"
    };

    private readonly DrawingService _drawingService;
    private readonly DrawingDoc _drawing;
    private readonly ModelDoc2 _model;
    private readonly IDictionary<string, string> _logicalToActual;

    public BreaklineService(
        DrawingService drawingService,
        IDictionary<string, string>? logicalToActual = null)
    {
        _drawingService =
            drawingService
            ?? throw new ArgumentNullException(
                nameof(drawingService));

        _drawing =
            drawingService.Drawing
            ?? throw new InvalidOperationException(
                "No active drawing.");

        _model =
            drawingService.Model
            ?? throw new InvalidOperationException(
                "No active drawing model.");

        _logicalToActual =
            logicalToActual
            ?? new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
    }

    public bool Apply(
        string logicalView,
        WedgeData wedge,
        DrawingData drawingData)
    {
        if (string.IsNullOrWhiteSpace(logicalView))
            return false;

        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        var actualName =
            ResolveActualName(
                logicalView);

        var view =
            ViewFinder.FindByName(
                _drawing,
                actualName);

        if (view is null)
        {
            Logger.Warn(
                $"[Breaklines] View '{logicalView}' " +
                $"('{actualName}') was not found.");

            return false;
        }

        try
        {
            var handler =
                new BreaklineHandler(
                    view,
                    _model);

            var ok =
                handler.ApplyBreakline(
                    logicalView,
                    wedge,
                    drawingData);

            if (!ok)
            {
                Logger.Warn(
                    $"[Breaklines] Apply failed for " +
                    $"'{logicalView}'.");
            }

            return ok;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[Breaklines] '{logicalView}' failed: " +
                ex.Message);

            return false;
        }
    }

    public void ApplyEnabled(
        WedgeData wedge,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        if (wedge is null)
            throw new ArgumentNullException(nameof(wedge));

        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        foreach (var logicalView in SupportedLogicalViews)
        {
            if (!profile.UseBreaklinesForView(
                    logicalView))
            {
                continue;
            }

            Apply(
                logicalView,
                wedge,
                drawingData);
        }
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

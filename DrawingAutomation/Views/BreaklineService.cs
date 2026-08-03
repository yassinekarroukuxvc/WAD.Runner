using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Interop;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Views.Breaklines;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Views;

public sealed class BreaklineService
{
    private readonly DrawingDoc _drawing;
    private readonly IDictionary<string, string> _logicalToActual;
    private readonly BreaklineLayoutCalculator _calculator = new();

    public BreaklineService(
        DrawingService drawingService,
        IDictionary<string, string>? logicalToActual = null)
    {
        if (drawingService is null)
            throw new ArgumentNullException(nameof(drawingService));

        _drawing = drawingService.Drawing
            ?? throw new InvalidOperationException("No active drawing.");

        _logicalToActual = logicalToActual
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public bool Apply(
        string logicalView,
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingData drawingData)
    {
        var actualViewName = ResolveActualName(logicalView);
        var view = ViewFinder.FindByName(_drawing, actualViewName);

        if (view is null)
        {
            Logger.Warn(
                $"[Breaklines] View '{logicalView}' ('{actualViewName}') was not found.");
            return false;
        }

        var scale = InteropCompat.GetScaleDecimalOr(view, 0.0);
        var behavior = DrawingWedgeModuleRegistry.Get(wedgeType).Behavior;

        if (!_calculator.TryCalculate(
                logicalView,
                wedge,
                drawingData,
                scale,
                behavior,
                out var layout,
                out var error))
        {
            Logger.Warn($"[Breaklines] '{logicalView}' was not changed: {error}");
            return false;
        }

        var breakline = GetSingleBreakline(view, logicalView);
        if (breakline is null)
            return false;

        try
        {
            view.BreakLineGap = layout.GapSheetMeters;

            if (!breakline.SetPosition(
                    layout.LowerPosition,
                    layout.UpperPosition))
            {
                Logger.Warn($"[Breaklines] SolidWorks rejected positions for '{logicalView}'.");
                return false;
            }

            Logger.Success(
                $"[Breaklines] '{logicalView}' -> " +
                $"lower={layout.LowerPosition:F6}, " +
                $"upper={layout.UpperPosition:F6}, " +
                $"gap={layout.GapSheetMeters:F6}, " +
                $"scale={layout.ViewScale:F6}.");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Breaklines] '{logicalView}' failed: {ex.Message}");
            return false;
        }
    }

    public void ApplyEnabled(
        WedgeType wedgeType,
        WedgeData wedge,
        DrawingData drawingData,
        DrawingProfile profile)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));
        if (drawingData is null) throw new ArgumentNullException(nameof(drawingData));
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        foreach (var logicalView in DrawingViewNames.LayoutOrder)
        {
            if (!profile.UsesBreakline(logicalView))
                continue;

            Apply(logicalView, wedgeType, wedge, drawingData);
        }
    }

    private static BreakLine? GetSingleBreakline(
        View view,
        string logicalView)
    {
        try
        {
            if (view.GetBreakLineCount2(out _) <= 0)
            {
                Logger.Warn($"[Breaklines] '{logicalView}' contains no breakline.");
                return null;
            }

            if (view.GetBreakLines() is not object[] breaklines ||
                breaklines.Length != 1 ||
                breaklines[0] is not BreakLine breakline)
            {
                Logger.Warn(
                    $"[Breaklines] '{logicalView}' must contain exactly one breakline.");
                return null;
            }

            return breakline;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Breaklines] Could not read '{logicalView}': {ex.Message}");
            return null;
        }
    }

    private string ResolveActualName(string logicalView)
        => _logicalToActual.TryGetValue(logicalView, out var mapped) &&
           !string.IsNullOrWhiteSpace(mapped)
            ? mapped
            : logicalView;
}

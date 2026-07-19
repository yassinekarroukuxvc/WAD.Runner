using System;
using System.Collections.Generic;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Interop;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Read-only geometry queries used by layout/scaling.
/// </summary>
public sealed class ViewGeometryService
{
    private readonly DrawingDoc _drawing;
    private readonly IDictionary<string, string> _logicalToActual;

    public ViewGeometryService(
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

    public bool FitsHeight(
        string logicalView,
        ScalePolicy policy)
    {
        if (policy is null)
            throw new ArgumentNullException(nameof(policy));

        return FitsHeight(
            logicalView,
            policy.FillRatioHeight,
            policy.TopMarginMm,
            policy.BottomMarginMm);
    }

    public bool FitsHeight(
        string logicalView,
        double fillRatioHeight,
        double topMarginMm,
        double bottomMarginMm)
    {
        if (!double.IsFinite(fillRatioHeight) ||
            fillRatioHeight <= 0.0 ||
            fillRatioHeight > 1.0)
        {
            return false;
        }

        if (!TryGetAvailableHeightMeters(
                topMarginMm,
                bottomMarginMm,
                out var availableHeight))
        {
            return false;
        }

        if (!TryGetOutlineHeightMeters(
                logicalView,
                out var viewHeight))
        {
            return false;
        }

        var fill =
            viewHeight /
            availableHeight;

        return
            double.IsFinite(fill)
            && fill <= fillRatioHeight + 1e-9;
    }

    public bool TryGetOutlineHeightMeters(
        string logicalView,
        out double height)
    {
        height = 0.0;

        if (string.IsNullOrWhiteSpace(logicalView))
            return false;

        var view =
            FindView(
                logicalView);

        if (view is null)
            return false;

        if (!InteropCompat.TryGetViewOutline(
                view,
                out _,
                out var y1,
                out _,
                out var y2))
        {
            return false;
        }

        var candidate =
            Math.Abs(
                y2 - y1);

        if (!double.IsFinite(candidate) ||
            candidate <= 0.0)
        {
            return false;
        }

        height =
            candidate;

        return true;
    }

    public bool TryGetAvailableHeightMeters(
        double topMarginMm,
        double bottomMarginMm,
        out double availableHeight)
    {
        availableHeight = 0.0;

        if (!double.IsFinite(topMarginMm) ||
            !double.IsFinite(bottomMarginMm) ||
            topMarginMm < 0.0 ||
            bottomMarginMm < 0.0)
        {
            return false;
        }

        try
        {
            var sheet =
                _drawing.GetCurrentSheet()
                as Sheet;

            if (sheet is null)
                return false;

            double width = 0.0;
            double height = 0.0;

            sheet.GetSize(
                ref width,
                ref height);

            if (!double.IsFinite(height) ||
                height <= 0.0)
            {
                return false;
            }

            var marginsMeters =
                (topMarginMm + bottomMarginMm) /
                1000.0;

            var candidate =
                height -
                marginsMeters;

            if (!double.IsFinite(candidate) ||
                candidate <= 0.0)
            {
                return false;
            }

            availableHeight =
                candidate;

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
}

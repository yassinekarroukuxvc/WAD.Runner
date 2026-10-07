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

    public void LogOriginAndOutline(string logicalViewName)
    {
        var view = FindView(logicalViewName);

        if (view == null)
        {
            Logger.Warn(
                $"[ViewDiagnostic] View '{logicalViewName}' was not found.");

            return;
        }

        try
        {
            var positionObject = view.Position;

            double originX = 0.0;
            double originY = 0.0;

            if (positionObject is double[] position &&
                position.Length >= 2)
            {
                originX = position[0];
                originY = position[1];
            }

            if (!InteropCompat.TryGetViewOutline(
                    view,
                    out var minX,
                    out var minY,
                    out var maxX,
                    out var maxY))
            {
                Logger.Warn(
                    $"[ViewDiagnostic] Could not read outline for " +
                    $"'{logicalViewName}'.");

                return;
            }

            var outlineCenterX =
                (minX + maxX) * 0.5;

            var outlineCenterY =
                (minY + maxY) * 0.5;

            Logger.Info(
                $"[ViewDiagnostic:{logicalViewName}] " +
                $"Origin=({originX:F6}, {originY:F6}), " +
                $"Outline=({minX:F6}, {minY:F6})-" +
                $"({maxX:F6}, {maxY:F6}), " +
                $"OutlineCenter=({outlineCenterX:F6}, {outlineCenterY:F6}), " +
                $"Offset=({outlineCenterX - originX:F6}, " +
                $"{outlineCenterY - originY:F6}).");
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[ViewDiagnostic] Failed for view " +
                $"'{logicalViewName}': {ex.Message}");
        }
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

    /// <summary>
    /// Visible outline of a view in sheet coordinates (meters),
    /// or null if the view or its outline is unavailable.
    /// </summary>
    public ViewRect? GetRect(
        string logicalView)
    {
        if (string.IsNullOrWhiteSpace(logicalView))
            return null;

        var view =
            FindView(
                logicalView);

        if (view is null)
            return null;

        if (!InteropCompat.TryGetViewOutline(
                view,
                out var minX,
                out var minY,
                out var maxX,
                out var maxY))
        {
            return null;
        }

        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY))
        {
            return null;
        }

        return new ViewRect(
            minX,
            minY,
            maxX,
            maxY);
    }

    /// <summary>
    /// Sheet area (meters) minus a uniform margin, or null if the
    /// sheet size cannot be read.
    /// </summary>
    public ViewRect? GetUsableSheetRect(
        double marginMm)
    {
        if (!double.IsFinite(marginMm) ||
            marginMm < 0.0)
        {
            return null;
        }

        try
        {
            var sheet =
                _drawing.GetCurrentSheet()
                as Sheet;

            if (sheet is null)
                return null;

            double width = 0.0;
            double height = 0.0;

            sheet.GetSize(
                ref width,
                ref height);

            if (!double.IsFinite(width) ||
                !double.IsFinite(height) ||
                width <= 0.0 ||
                height <= 0.0)
            {
                return null;
            }

            var margin =
                marginMm / 1000.0;

            if (width <= margin * 2.0 ||
                height <= margin * 2.0)
            {
                return null;
            }

            return new ViewRect(
                margin,
                margin,
                width - margin,
                height - margin);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks that every view in <paramref name="views"/> is inside the
    /// usable sheet area, does not overlap the other views in that list,
    /// and does not overlap any view in <paramref name="others"/>.
    /// Views that cannot be measured are skipped.
    /// </summary>
    public bool IsLayoutClean(
        IReadOnlyList<string> views,
        IReadOnlyList<string> others,
        LayoutFitPolicy fit,
        out string reason)
    {
        if (views is null)
            throw new ArgumentNullException(nameof(views));

        if (others is null)
            throw new ArgumentNullException(nameof(others));

        if (fit is null)
            throw new ArgumentNullException(nameof(fit));

        reason = string.Empty;

        var sheet =
            GetUsableSheetRect(
                fit.SheetMarginMm);

        if (sheet is null)
        {
            // Without a sheet size nothing can be judged; do not block.
            reason = "sheet size unavailable";
            return true;
        }

        var gap =
            fit.ViewGapMm / 1000.0;

        var measured =
            new List<KeyValuePair<string, ViewRect>>();

        foreach (var name in views)
        {
            var rect =
                GetRect(
                    name);

            if (rect is null)
                continue;

            if (!rect.IsInside(sheet))
            {
                reason =
                    $"'{name}' {rect} exceeds sheet {sheet}";

                return false;
            }

            measured.Add(
                new KeyValuePair<string, ViewRect>(
                    name,
                    rect));
        }

        for (var i = 0; i < measured.Count; i++)
        {
            for (var j = i + 1; j < measured.Count; j++)
            {
                if (measured[i].Value.Inflate(gap)
                        .Overlaps(measured[j].Value))
                {
                    reason =
                        $"'{measured[i].Key}' overlaps '{measured[j].Key}'";

                    return false;
                }
            }
        }

        foreach (var item in measured)
        {
            foreach (var otherName in others)
            {
                if (string.IsNullOrWhiteSpace(otherName) ||
                    ContainsName(views, otherName))
                {
                    continue;
                }

                var otherRect =
                    GetRect(
                        otherName);

                if (otherRect is null)
                    continue;

                if (item.Value.Inflate(gap)
                        .Overlaps(otherRect))
                {
                    reason =
                        $"'{item.Key}' overlaps '{otherName}'";

                    return false;
                }
            }
        }

        return true;
    }

    private static bool ContainsName(
        IReadOnlyList<string> names,
        string candidate)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(
                    names[i],
                    candidate,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
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
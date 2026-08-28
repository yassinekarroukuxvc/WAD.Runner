using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

using SwAnnotation = SolidWorks.Interop.sldworks.Annotation;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

public sealed class DrawingAnnotationStateReader
{
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> CollectExistingDisplayDimensionNames(
        ModelDoc2 drawingModel,
        AnnotationViewNameMap viewNames,
        bool activateEachView)
    {
        if (drawingModel is null)
            throw new ArgumentNullException(nameof(drawingModel));

        if (viewNames is null)
            throw new ArgumentNullException(nameof(viewNames));

        var byView = new Dictionary<string, IReadOnlyCollection<string>>(
            StringComparer.OrdinalIgnoreCase);

        var requestedNames = viewNames
            .AllNominalNames()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING ||
            drawingModel is not DrawingDoc drawing)
        {
            foreach (var viewName in requestedNames)
                byView[viewName] = Array.Empty<string>();

            return byView;
        }

        var drawingViews = BuildDrawingViewMap(drawing);

        foreach (var viewName in requestedNames)
        {
            if (!drawingViews.TryGetValue(viewName, out var view))
            {
                byView[viewName] = Array.Empty<string>();
                continue;
            }

            if (activateEachView)
            {
                try
                {
                    drawingModel.Extension?.SelectByID2(
                        viewName,
                        "DRAWINGVIEW",
                        0,
                        0,
                        0,
                        false,
                        0,
                        null,
                        0);
                }
                catch
                {
                    // Best effort only.
                }
            }

            byView[viewName] = EnumerateDisplayDimensionFullNames(view)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return byView;
    }

    private static Dictionary<string, View> BuildDrawingViewMap(
        DrawingDoc drawing)
    {
        var result = new Dictionary<string, View>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var sheetView = drawing.GetFirstView() as View;
            var view = sheetView?.GetNextView() as View;

            while (view is not null)
            {
                var name = SafeGetViewName(view).Trim();

                if (!string.IsNullOrWhiteSpace(name) &&
                    !result.ContainsKey(name))
                {
                    result.Add(name, view);
                }

                view = view.GetNextView() as View;
            }
        }
        catch
        {
            // Return everything collected before the COM failure.
        }

        return result;
    }

    private static string SafeGetViewName(
        View view)
    {
        try
        {
            return view?.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> EnumerateDisplayDimensionFullNames(
        View view)
    {
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var displayDimension in EnumerateDisplayDimensions(view))
        {
            Dimension? dimension = null;

            try
            {
                dimension = displayDimension.GetDimension() as Dimension;
            }
            catch
            {
                // Ignore malformed dimensions.
            }

            if (dimension is null)
                continue;

            string fullName;

            try
            {
                fullName = dimension.FullName ?? string.Empty;
            }
            catch
            {
                continue;
            }

            fullName = fullName.Trim();

            if (!string.IsNullOrWhiteSpace(fullName) &&
                seen.Add(fullName))
            {
                yield return fullName;
            }
        }
    }

    private static IReadOnlyList<DisplayDimension> EnumerateDisplayDimensions(
        View view)
    {
        var result = new List<DisplayDimension>(64);
        var seen = new HashSet<DisplayDimension>();

        try
        {
            var rawAnnotations = view.GetAnnotations();

            if (rawAnnotations is object[] annotations)
            {
                foreach (var item in annotations)
                {
                    if (item is not SwAnnotation annotation)
                        continue;

                    try
                    {
                        if (annotation.GetSpecificAnnotation() is DisplayDimension displayDimension &&
                            seen.Add(displayDimension))
                        {
                            result.Add(displayDimension);
                        }
                    }
                    catch
                    {
                        // Continue with remaining annotations.
                    }
                }
            }
        }
        catch
        {
            // Continue with GetDisplayDimensions().
        }

        try
        {
            var rawDisplayDimensions = view.GetDisplayDimensions();

            if (rawDisplayDimensions is object[] displayDimensions)
            {
                foreach (var item in displayDimensions)
                {
                    if (item is DisplayDimension displayDimension &&
                        seen.Add(displayDimension))
                    {
                        result.Add(displayDimension);
                    }
                }
            }
        }
        catch
        {
            // Return anything already collected through GetAnnotations().
        }

        return result;
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Engine;

public sealed class DrawingAnnotationStateReader
{
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> CollectExistingDisplayDimensionNames(
        ModelDoc2 drawingModel,
        AnnotationViewNameMap viewNames,
        bool activateEachView)
    {
        if (drawingModel is null) throw new ArgumentNullException(nameof(drawingModel));
        if (viewNames is null) throw new ArgumentNullException(nameof(viewNames));

        var byView = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        if (drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
        {
            foreach (var viewName in viewNames.AllNominalNames())
                byView[viewName] = Array.Empty<string>();

            return byView;
        }

        foreach (var viewName in viewNames.AllNominalNames())
        {
            var view = TryGetDrawingViewByName(drawingModel, viewName);
            if (view == null)
            {
                byView[viewName] = Array.Empty<string>();
                continue;
            }

            if (activateEachView)
            {
                try
                {
                    drawingModel.Extension?.SelectByID2(viewName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                }
                catch
                {
                    // Activation is best-effort only. Dimension enumeration can still work without it.
                }
            }

            byView[viewName] = EnumerateDisplayDimensionFullNames(view)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return byView;
    }

    private static View? TryGetDrawingViewByName(ModelDoc2 drawingModel, string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName)) return null;
        if (drawingModel is not DrawingDoc drawing) return null;

        try
        {
            var view = drawing.GetFirstView() as View;
            view = view?.GetNextView() as View;

            while (view != null)
            {
                if (string.Equals(SafeGetViewName(view), viewName, StringComparison.OrdinalIgnoreCase))
                    return view;

                view = view.GetNextView() as View;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string SafeGetViewName(View view)
    {
        try { return view?.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static IEnumerable<string> EnumerateDisplayDimensionFullNames(View view)
    {
        object obj;
        try
        {
            obj = view.GetDisplayDimensions();
        }
        catch
        {
            yield break;
        }

        if (obj is not object[] displayDimensions || displayDimensions.Length == 0)
            yield break;

        foreach (var item in displayDimensions)
        {
            if (item is not DisplayDimension displayDimension)
                continue;

            Dimension? dimension = null;
            try { dimension = displayDimension.GetDimension() as Dimension; }
            catch { }

            if (dimension == null)
                continue;

            string fullName;
            try { fullName = dimension.FullName ?? string.Empty; }
            catch { continue; }

            if (!string.IsNullOrWhiteSpace(fullName))
                yield return fullName.Trim();
        }
    }
}

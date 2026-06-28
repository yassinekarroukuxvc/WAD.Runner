using System;
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.DrawingAutomation.Views;

internal static class ViewFinder
{


    public static View? FindByName(DrawingDoc drawing, string viewName)
    {
        if (drawing is null || string.IsNullOrWhiteSpace(viewName)) return null;

        var v = drawing.GetFirstView() as View;
        while (v is not null)
        {
            try
            {
                var name = v.GetName2();
                if (!string.IsNullOrWhiteSpace(name) &&
                    string.Equals(name.Trim(), viewName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return v;
            }
            catch {  }

            v = v.GetNextView() as View;
        }

        return null;
    }
}

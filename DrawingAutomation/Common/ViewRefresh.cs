using System;
using WAD.Runner.Application;                          // Logger
using WAD.Runner.DrawingAutomation.SolidWorks;         // DrawingService
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.DrawingAutomation.Common;

/// <summary>
/// Centralized rebuild/refresh/zoom helpers.
/// </summary>
public static class ViewRefresh
{
    /// <summary>
    /// Strong refresh: edit rebuild, redraw, zoom-to-sheet.
    /// </summary>
    public static void Hard(SldWorks swApp, DrawingService ds)
    {
        if (swApp is null) throw new ArgumentNullException(nameof(swApp));
        if (ds is null) throw new ArgumentNullException(nameof(ds));
        if (ds.Model is null) return;

        try
        {
            // Prefer EditRebuild3 for drawings; fall back to ForceRebuild3
            bool ok = ds.Model.EditRebuild3();
            if (!ok) Logger.Warn("EditRebuild3 returned false (continuing).");
        }
        catch
        {
            try { ds.Model.ForceRebuild3(false); } catch { }
        }

        try { ds.Model.GraphicsRedraw2(); } catch { }
        try { ds.ZoomToSheet(); } catch { }
    }

    /// <summary>
    /// Light refresh: redraw and zoom-fit only.
    /// </summary>
    public static void Light(DrawingService ds)
    {
        if (ds is null) throw new ArgumentNullException(nameof(ds));
        if (ds.Model is null) return;

        try { ds.Model.GraphicsRedraw2(); } catch { }
        try { ds.Model.ViewZoomtofit2(); } catch { }
    }
}

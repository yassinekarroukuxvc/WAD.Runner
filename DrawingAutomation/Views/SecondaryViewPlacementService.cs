using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.Interop;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;


public sealed class SecondaryViewPlacementService
{
    private readonly DrawingService _ds;
    private readonly DrawingDoc _drawing;
    private readonly ModelDoc2 _model;
    private readonly IDictionary<string, string> _logicalToActual;

    public SecondaryViewPlacementService(
        DrawingService ds,
        IDictionary<string, string>? logicalToActual = null)
    {
        _ds = ds ?? throw new ArgumentNullException(nameof(ds));
        _drawing = ds.Drawing ?? throw new InvalidOperationException("No active drawing.");
        _model = ds.Model ?? throw new InvalidOperationException("No active drawing model.");
        _logicalToActual = logicalToActual ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }


    public bool TrySetViewPositionInches(string logicalKey, double xIn, double yIn, bool clampToSheet = true)
    {
        if (string.IsNullOrWhiteSpace(logicalKey))
            return Logger.WarnAndReturnFalse("[SecondaryView] Logical key is null/empty.");

        var actualName = _logicalToActual.TryGetValue(logicalKey, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
            ? mapped
            : logicalKey;

        var view = ViewFinder.FindByName(_drawing, actualName);
        if (view is null)
        {
            Logger.Warn($"[SecondaryView] View '{actualName}' (logical '{logicalKey}') not found on sheet.");
            return false;
        }


        double x_m = xIn * 0.0254;
        double y_m = yIn * 0.0254;

        try
        {
            if (clampToSheet && TryGetSheetSize(out var w, out var h))
                (x_m, y_m) = ClampToSheet(x_m, y_m, w, h);


            InteropCompat.TryUnlock(view);

            view.Position = new[] { x_m, y_m };
            TryRebuild();

            Logger.Info(
                $"[SecondaryView] Set view '{actualName}' (logical '{logicalKey}') to " +
                $"({xIn:0.###}\", {yIn:0.###}\") → ({x_m:F4}, {y_m:F4}) m.");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[SecondaryView] TrySetViewPositionInches failed for '{actualName}': {ex.Message}");
            return false;
        }
    }


    public static void RunMacroForViewIfAvailable(
        SldWorks swApp,
        string? macroFile,
        string logicalViewName,
        string sketchName,
        double xIn,
        double yIn,
        IDictionary<string, string>? logicalToActual = null)
    {
        try
        {
            if (swApp is null)
            {
                Logger.Warn("[SecondaryView] RunMacroForViewIfAvailable: swApp is null.");
                return;
            }

            if (string.IsNullOrWhiteSpace(macroFile) || !File.Exists(macroFile))
            {
                Logger.Warn($"[SecondaryView] Macro file not found; skipping macro for view '{logicalViewName}'. (Expected at: {macroFile ?? "(null)"} )");
                return;
            }


            string actualViewName = logicalViewName;
            if (logicalToActual != null &&
                logicalToActual.TryGetValue(logicalViewName, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped))
            {
                actualViewName = mapped;
            }

            var moduleCandidates = new[] { "Macro1", "Module1" };
            var ran = false;

            foreach (var mod in moduleCandidates)
            {
                var runner = new SolidWorksMacroRunner(macroFile, mod, "main");
                runner.PrepareArgs(actualViewName, sketchName, xIn, yIn);
                if (runner.Run(swApp))
                {
                    Logger.Success($"[SecondaryView] Macro ran OK for view '{actualViewName}' using module '{mod}'.");
                    ran = true;
                    break;
                }
            }

            if (!ran)
            {
                Logger.Warn(
                    $"[SecondaryView] Macro did not run successfully for view '{actualViewName}'. " +
                    $"Tried modules: {string.Join(", ", moduleCandidates)}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[SecondaryView] Macro for logical view '{logicalViewName}' skipped: {ex.Message}");
        }
    }


    private void TryRebuild()
    {
        try { _model.EditRebuild3(); } catch { }
        try { _model.GraphicsRedraw2(); } catch { }
    }

    private bool TryGetSheetSize(out double w, out double h)
    {
        w = h = 0;
        try
        {
            var sheet = _drawing.GetCurrentSheet() as Sheet;
            double ww = 0, hh = 0;
            sheet?.GetSize(ref ww, ref hh);
            w = ww;
            h = hh;
            return w > 0 && h > 0;
        }
        catch
        {
            return false;
        }
    }

    private static (double X, double Y) ClampToSheet(double x, double y, double w, double h)
    {
        x = Math.Clamp(x, 0.0, w);
        y = Math.Clamp(y, 0.0, h);
        return (x, y);
    }
}

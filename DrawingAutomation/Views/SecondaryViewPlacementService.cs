using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData
using WAD.Runner.DrawingAutomation.Interop;           // InteropCompat, ViewFinder
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Helper for "secondary" views (typically Detail / Section) where we:
/// - Position a given view in inches (logical view name → actual view)
/// - Optionally run a VBA macro that uses view/sketch name + coordinates
/// Intended to complement ViewPlacementService (which is mm + DrawingData based).
/// </summary>
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

    /// <summary>
    /// Places a view (by logical key) using inches as input.
    /// - Resolves logicalKey → actual SW view name via the map (fallback: logicalKey)
    /// - Inches → meters
    /// - Optionally clamps to sheet bounds
    /// - Breaks alignment + unlocks
    /// - Sets position and rebuilds
    /// </summary>
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

        // inches → meters
        double x_m = xIn * 0.0254;
        double y_m = yIn * 0.0254;

        try
        {
            if (clampToSheet && TryGetSheetSize(out var w, out var h))
                (x_m, y_m) = ClampToSheet(x_m, y_m, w, h);

            // unlock / break alignment (best effort)
            //InteropCompat.TryBreakAlignment(view);
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

    // ───────────────────────────── MACRO LAUNCHER ─────────────────────────────

    /// <summary>
    /// Runs a VBA macro for a given view (if the macro file exists).
    /// Writes arguments (viewName, sketchName, X_IN, Y_IN) to the macro args file.
    /// View is resolved from logicalViewName via the map if provided.
    /// </summary>
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

            // Resolve logical → actual for logging / macro arg
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

    // ───────────────────────────── helpers ─────────────────────────────

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

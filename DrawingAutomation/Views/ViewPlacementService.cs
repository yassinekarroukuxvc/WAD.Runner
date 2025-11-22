using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData, ViewConfig
using WAD.Runner.DrawingAutomation.Interop;           // InteropCompat
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService

namespace WAD.Runner.DrawingAutomation.Views
{
    /// <summary>
    /// Applies scale and position to drawing views based on DrawingData.Views.
    /// - Supports any logical view key ("Front", "Side", "Top", "Detail", "Section", etc.)
    /// - Uses an optional logical→actual name map to resolve SolidWorks view names.
    /// - Breaks alignment and unlocks the view (best effort)
    /// - Applies Scale (if &gt; 0) and PositionMm (mm → meters, clamped to sheet)
    /// - Rebuilds and redraws after placement.
    /// </summary>
    public sealed class ViewPlacementService
    {
        private readonly DrawingService _ds;
        private readonly DrawingDoc _drawing;
        private readonly ModelDoc2 _model;
        private readonly IDictionary<string, string> _logicalToActual;

        public ViewPlacementService(DrawingService ds, IDictionary<string, string>? logicalToActual = null)
        {
            _ds = ds ?? throw new ArgumentNullException(nameof(ds));
            _drawing = ds.Drawing ?? throw new InvalidOperationException("No active drawing.");
            _model = ds.Model ?? throw new InvalidOperationException("No active drawing model.");
            _logicalToActual = logicalToActual ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Place a single view by logical key (e.g. "Front", "Side", "Top", "Detail", "Section").
        /// </summary>
        public bool Apply(string logicalKey, DrawingData drawingData)
        {
            if (string.IsNullOrWhiteSpace(logicalKey) || drawingData is null) return false;
            if (!drawingData.Views.TryGetValue(logicalKey, out var viewCfg) || viewCfg is null) return false;

            var actualName = _logicalToActual.TryGetValue(logicalKey, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : logicalKey;

            var view = ViewFinder.FindByName(_drawing, actualName);
            if (view is null)
            {
                Logger.Warn($"[{logicalKey}] View '{actualName}' not found on sheet (skipping).");
                return false;
            }

            try
            {
                // 1) Break alignment / unlock pos (best effort)
                InteropCompat.TryBreakAlignment(view);
                InteropCompat.TryUnlock(view);

                // 2) Scale
                if (viewCfg.Scale > 0)
                {
                    InteropCompat.TrySetScale(view, viewCfg.Scale);
                }

                // 3) Position (mm -> m)
                var x_mm = (viewCfg.PositionMm is { Length: >= 2 }) ? viewCfg.PositionMm[0] : 0.0;
                var y_mm = (viewCfg.PositionMm is { Length: >= 2 }) ? viewCfg.PositionMm[1] : 0.0;

                var x_m = x_mm / 1000.0;
                var y_m = y_mm / 1000.0;

                if (TryGetSheetSize(out var sw, out var sh))
                {
                    (x_m, y_m) = ClampToSheet(x_m, y_m, sw, sh);
                }

                view.Position = new[] { x_m, y_m };

                // 4) Rebuild + redraw
                HardRebuild();

                Logger.Info(
                    $"[{logicalKey}] Placed view '{actualName}' at " +
                    $"({x_mm.ToString("0.###", CultureInfo.InvariantCulture)} mm, " +
                    $"{y_mm.ToString("0.###", CultureInfo.InvariantCulture)} mm), " +
                    $"scale={(viewCfg.Scale > 0 ? viewCfg.Scale.ToString("0.###", CultureInfo.InvariantCulture) : "unchanged")}.");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[{logicalKey}] Failed to place view '{actualName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Convenience: place Front / Side / Top based on DrawingData.Views.
        /// </summary>
        public void ApplyFrontSideTop(DrawingData drawingData)
        {
            Apply("Front", drawingData);
            Apply("Side", drawingData);
            Apply("Top", drawingData);
        }

        /// <summary>
        /// Convenience: place Detail / Section based on DrawingData.Views.
        /// </summary>
        public void ApplyDetailAndSection(DrawingData drawingData)
        {
            Apply("Detail", drawingData);
            Apply("Section", drawingData);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private void HardRebuild()
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
}

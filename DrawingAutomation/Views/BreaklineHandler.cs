using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using WAD.Runner.Application;                         // Logger
using WAD.Runner.DataManagement.Domain.Dimensions;    // DimensionKey, Dimension
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData, DrawingType, ViewConfig
using WAD.Runner.DataManagement.Domain.Units;         // Quantity, UnitKind
using WAD.Runner.DataManagement.Domain.Wedge;         // WedgeData, WedgeSubclass

namespace WAD.Runner.DrawingAutomation.Views
{
    /// <summary>
    /// Breakline positioning for FG Production/Customer and PGB Production.
    /// Inputs:
    ///  - TL (mm) required
    ///  - KValue (mm) optional for FG
    ///  - E (mm) NOT used for these modes (Overlay uses it; omitted here)
    ///
    /// Overrides (optional) via DrawingData.Views[viewName].Params (double values):
    ///  - "breakline_gap_mm"           : sheet gap in millimeters
    ///  - "detail_inset_mm"            : inset for detail/section (sheet mm)
    ///
    ///  FG tuning:
    ///  - "fg_fallback_pct"            : default 0.40 (when K is missing)
    ///  - "fg_front_offset_pct"        : default 0.03
    ///  - "fg_front_upper_pct"         : default 0.13
    ///
    ///  PGB tuning:
    ///  - "pgb_fallback_pct"           : default 0.35
    ///  - "pgb_front_offset_pct"       : default 0.02
    ///  - "pgb_front_upper_pct"        : default 0.12
    ///
    /// If a param is absent, defaults apply.
    /// </summary>
    public sealed class BreaklineHandler
    {
        private readonly View _view;
        private readonly ModelDoc2 _model;

        public BreaklineHandler(View swView, ModelDoc2 model)
        {
            _view = swView;
            _model = model;
        }

        // --------------------------- Public API ---------------------------

        /// <summary>Sets the view breakline gap in sheet meters.</summary>
        public bool SetBreaklineGap(double gapSheetMeters)
        {
            if (!IsReady()) return false;
            try
            {
                _view.BreakLineGap = gapSheetMeters;
                Logger.Success($"Breakline gap set to {gapSheetMeters:F3} m (sheet).");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"SetBreaklineGap failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply breakline for FG Production/Customer or PGB Production.
        /// Pass logical view names you already use in ViewNameMap (e.g., "Front","Side","Detail","Section").
        /// </summary>
        public bool ApplyBreakline(string viewName, DrawingType drawingType, WedgeSubclass subclass, WedgeData wedge, DrawingData drawData)
        {
            if (!IsReady()) return false;

            // Apply per-view gap override (if any)
            TryApplyConfiguredGap(drawData, viewName);

            // Detail/Section share symmetric logic
            if (IsDetail(viewName) || IsSection(viewName))
                return SetDetailOrSectionBreakline(wedge, drawData, viewName, drawingType, subclass);

            // Front/Side are subclass + drawingType dependent
            if (IsFront(viewName) || IsSide(viewName))
            {
                return (drawingType, subclass) switch
                {
                    (DrawingType.Production, WedgeSubclass.FG) => SetFrontSide_FG(wedge, drawData),
                    (DrawingType.Customer, WedgeSubclass.FG) => SetFrontSide_FG(wedge, drawData),

                    (DrawingType.Production, WedgeSubclass.PGB) => SetFrontSide_PGB(wedge, drawData),

                    // Out of scope combos fall back to FG rules (safe) — adjust if you add more strategies later.
                    (DrawingType.Customer, WedgeSubclass.PGB) => SetFrontSide_PGB(wedge, drawData),

                    _ => false
                };
            }

            Logger.Warn($"ApplyBreakline: unrecognized view key '{viewName}'. Expected Front/Side/Detail/Section.");
            return false;
        }

        // --------------------------- Detail / Section (shared) ---------------------------

        private bool SetDetailOrSectionBreakline(WedgeData wedge, DrawingData drawData, string viewName, DrawingType drawingType, WedgeSubclass subclass)
        {
            var bl = GetValidatedBreakline();
            if (bl == null) return false;

            var tl_m = MmToMeters(GetLengthMm(wedge, "TL"));
            if (tl_m <= 0)
            {
                Logger.Warn("TL is missing/invalid for Detail/Section breakline.");
                return false;
            }

            var scale = SafeScale();

            // View-scoped override first; else look for mode-scoped; else default.
            var inset_mm = ResolveViewParam(drawData, viewName, "detail_inset_mm",
                             ResolveModeParam(drawData, drawingType, subclass, "detail_inset_mm",
                                 Defaults.DetailInsetMm));

            var inset_sheet_m = MmToMeters(inset_mm);

            var halfSpan_sheet = (tl_m * 0.5) * scale;
            var lower = -halfSpan_sheet + inset_sheet_m;
            var upper = +halfSpan_sheet;

            var ok = TrySetBreakline(bl, lower, upper);
            if (ok) Logger.Success($"Detail/Section breakline → lower={lower:F4}, upper={upper:F4} (sheet m)");
            return ok;
        }

        // --------------------------- Front/Side: FG ---------------------------

        private bool SetFrontSide_FG(WedgeData wedge, DrawingData drawData)
        {
            var bl = GetValidatedBreakline();
            if (bl == null) return false;

            var tl_mm = GetLengthMm(wedge, "TL");
            var tl_m = MmToMeters(tl_mm);
            if (tl_m <= 0)
            {
                Logger.Warn("TL is missing/invalid for FG Front/Side breakline.");
                return false;
            }

            var scale = SafeScale();

            // FG tunables (percentages)
            var fallbackPct = ResolveGlobalParam(drawData, "fg_fallback_pct", Defaults.FG_FallbackPct);
            var offsetPct = ResolveGlobalParam(drawData, "fg_front_offset_pct", Defaults.FG_FrontOffsetPct);
            var upperPct = ResolveGlobalParam(drawData, "fg_front_upper_pct", Defaults.FG_FrontUpperPct);

            // Prefer KValue if present; clamp to TL/2
            double k_m;
            if (TryGetKMeters(wedge, tl_mm, out k_m))
            {
                Logger.Blue($"[FG] Engraving start (K-Value) = {k_m:F6} m");
            }
            else
            {
                k_m = Math.Min((double)tl_mm * (double)fallbackPct / 1000.0, tl_m * 0.5);
                Logger.Warn($"[FG] K-Value missing; fallback → TL * {fallbackPct:P0} = {k_m:F6} m");
            }

            var lower = (tl_m * 0.5 - k_m + tl_m * (double)offsetPct) * scale;
            var upper = (tl_m * 0.5 - tl_m * (double)upperPct) * scale;

            var ok = TrySetBreakline(bl, lower, upper);
            if (ok) Logger.Success($"[FG] Front/Side breakline → lower={lower:F4}, upper={upper:F4} (sheet m)");
            return ok;
        }

        // --------------------------- Front/Side: PGB ---------------------------

        private bool SetFrontSide_PGB(WedgeData wedge, DrawingData drawData)
        {
            var bl = GetValidatedBreakline();
            if (bl == null) return false;

            var tl_mm = GetLengthMm(wedge, "TL");
            var tl_m = MmToMeters(tl_mm);
            if (tl_m <= 0)
            {
                Logger.Warn("TL is missing/invalid for PGB Front/Side breakline.");
                return false;
            }

            var scale = SafeScale();

            var fallbackPct = ResolveGlobalParam(drawData, "pgb_fallback_pct", Defaults.PGB_FallbackPct);
            var offsetPct = ResolveGlobalParam(drawData, "pgb_front_offset_pct", Defaults.PGB_FrontOffsetPct);
            var upperPct = ResolveGlobalParam(drawData, "pgb_front_upper_pct", Defaults.PGB_FrontUpperPct);

            // PGB: always fallback (no engraving)
            var k_m = Math.Min((double)tl_mm * (double)fallbackPct / 1000.0, tl_m * 0.5);

            var lower = (tl_m * 0.5 - k_m + tl_m * (double)offsetPct) * scale;
            var upper = (tl_m * 0.5 - tl_m * (double)upperPct) * scale;

            var ok = TrySetBreakline(bl, lower, upper);
            if (ok) Logger.Success($"[PGB] Front/Side breakline → lower={lower:F4}, upper={upper:F4} (sheet m)");
            return ok;
        }

        // --------------------------- Helpers ---------------------------

        private bool IsReady() => _view != null && _model != null;

        private static bool IsFront(string v) => v.Equals("Front", StringComparison.OrdinalIgnoreCase);
        private static bool IsSide(string v) => v.Equals("Side", StringComparison.OrdinalIgnoreCase);
        private static bool IsDetail(string v) => v.Equals("Detail", StringComparison.OrdinalIgnoreCase);
        private static bool IsSection(string v) => v.Equals("Section", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// If DrawingData contains a configured gap for this view, apply it.
        /// Looks for Views[viewName].Params["breakline_gap_mm"] (sheet millimeters).
        /// </summary>
        private void TryApplyConfiguredGap(DrawingData drawData, string viewName)
        {
            if (drawData?.Views == null || !drawData.Views.TryGetValue(viewName, out var vc) || vc == null) return;

            if (TryGetParam(vc.Params, "breakline_gap_mm", out var gap_mm) && gap_mm > 0)
            {
                var gap_m = MmToMeters(gap_mm);
                if (!SetBreaklineGap(gap_m))
                    Logger.Warn($"Failed to apply configured gap for '{viewName}' ({gap_mm} mm).");
            }
        }

        /// <summary>Return last breakline if present, else null (with a friendly warning).</summary>
        private BreakLine? GetValidatedBreakline()
        {
            if (_view == null) return null;
            try
            {
                var count = _view.GetBreakLineCount2(out _);
                if (count <= 0)
                {
                    Logger.Warn("View has no breaklines to position.");
                    return null;
                }
                return _view.IGetBreakLines(count - 1);
            }
            catch (Exception ex)
            {
                Logger.Warn($"GetValidatedBreakline failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Length in mm for a key (0 if absent/invalid).</summary>
        private static decimal GetLengthMm(WedgeData wedge, string key)
        {
            try
            {
                var d = wedge.TryGet(DimensionKey.From(key));
                if (d is null) return 0m;
                if (d.Nominal.Unit != UnitKind.Millimeter) return 0m;
                return d.Nominal.Value;
            }
            catch { return 0m; }
        }

        private static double MmToMeters(decimal mm) => (double)(mm / 1000m);

        /// <summary>Safe view scale (>= 1e-6), defaults to 1.0 on error.</summary>
        private double SafeScale()
        {
            try
            {
                var s = _view?.ScaleDecimal ?? 1.0;
                return s > 1e-6 ? s : 1.0;
            }
            catch { return 1.0; }
        }

        private static bool TrySetBreakline(BreakLine bl, double lower, double upper)
        {
            try { return bl.SetPosition(lower, upper); }
            catch { return false; }
        }

        /// <summary>
        /// Try to read K in meters; clamps to [0, TL/2].
        /// </summary>
        private static bool TryGetKMeters(WedgeData wedge, decimal tl_mm, out double k_m)
        {
            k_m = 0.0;
            try
            {
                var k = wedge.KValue;
                if (k == null) return false;
                var kv_mm = k.ValueMm.Value; // Quantity in mm by contract
                if (kv_mm <= 0m) return false;

                var tl_half_mm = tl_mm * 0.5m;
                var clamped_mm = kv_mm > tl_half_mm ? tl_half_mm : kv_mm;
                k_m = MmToMeters(clamped_mm);
                return k_m > 0;
            }
            catch { return false; }
        }

        // ---- parameter resolution helpers ----

        private static bool TryGetParam(IReadOnlyDictionary<string, double> src, string key, out double val)
        {
            foreach (var kv in src)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    val = kv.Value;
                    return true;
                }
            }
            val = 0;
            return false;
        }

        private static double ResolveViewParam(DrawingData dd, string viewName, string key, double fallback)
        {
            if (dd?.Views != null && dd.Views.TryGetValue(viewName, out var vc) && vc != null)
                if (TryGetParam(vc.Params, key, out var v) && double.IsFinite(v) && v > 0) return v;
            return fallback;
        }

        /// <summary>
        /// Mode-scoped key: $"{drawingType}_{subclass}_{paramName}" in any View.Params (global bag).
        /// We scan all views for a matching key so you can store mode knobs anywhere.
        /// </summary>
        private static double ResolveModeParam(DrawingData dd, DrawingType dt, WedgeSubclass sc, string paramName, double fallback)
        {
            var wanted = $"{dt}_{sc}_{paramName}"; // e.g., "Production_FG_detail_inset_mm"
            if (dd?.Views != null)
            {
                foreach (var vc in dd.Views.Values)
                    if (TryGetParam(vc.Params, wanted, out var v) && double.IsFinite(v) && v > 0) return v;
            }
            return fallback;
        }

        private static double ResolveGlobalParam(DrawingData dd, string key, double fallback)
        {
            if (dd?.Views != null)
            {
                foreach (var vc in dd.Views.Values)
                    if (TryGetParam(vc.Params, key, out var v) && double.IsFinite(v) && v >= 0) return v;
            }
            return fallback;
        }
        private static double MmToMeters(double mm) => mm / 1000.0;

        // Local defaults (mm/pct) to avoid project-wide constants
        private static class Defaults
        {
            // Detail/Section inset in sheet millimeters
            public const double DetailInsetMm = 40;

            // FG percentages (0..1)
            public const double FG_FallbackPct = 0.40;
            public const double FG_FrontOffsetPct = 0.020;
            public const double FG_FrontUpperPct = 0.050;

            // PGB percentages (0..1)
            public const double PGB_FallbackPct = 0.35;
            public const double PGB_FrontOffsetPct = 0.020;
            public const double PGB_FrontUpperPct = 0.050;
        }
    }
}

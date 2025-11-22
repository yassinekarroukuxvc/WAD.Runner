// DrawingAutomation/Views/AnnotationPositionService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData
using WAD.Runner.DataManagement.Domain.Wedge;         // WedgeData
using WAD.Runner.DataManagement.Domain.Dimensions;    // DimensionKey
using WAD.Runner.DataManagement.Domain.Units;         // Quantity, UnitKind
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService

// Resolve SolidWorks vs Domain type name clash:
using SwDimension = SolidWorks.Interop.sldworks.Dimension;

namespace WAD.Runner.DrawingAutomation.Views
{
    /// <summary>
    /// Positions annotations (dimensions) in specific views.
    /// - Plan positions are sheet millimeters; API uses meters.
    /// - Moves the note box via Annotation.SetPosition2.
    /// - Matching: exact token (before '@') → relaxed name → numeric; pick nearest when multiple.
    /// </summary>
    public sealed class AnnotationPositioner
    {
        private readonly DrawingService _ds;
        private readonly IDictionary<string, string> _nameMap;

        private const bool DebugDumpOnMiss = false;

        public AnnotationPositioner(DrawingService ds, IDictionary<string, string> nameMap)
        {
            _ds = ds ?? throw new ArgumentNullException(nameof(ds));
            _nameMap = nameMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Apply(WedgeData wedge, DrawingData drawing, IEnumerable<Plan> planned)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (drawing is null) throw new ArgumentNullException(nameof(drawing));
            if (planned is null) return;

            var dd = _ds.Drawing as DrawingDoc;
            var model = _ds.Model as ModelDoc2;
            if (dd is null || model is null)
            {
                Logger.Warn("[DimPos] Skipped: drawing or model is null.");
                return;
            }

            var byView = planned.GroupBy(p => p.View, StringComparer.OrdinalIgnoreCase);

            int applied = 0, missed = 0;

            foreach (var grp in byView)
            {
                var view = FindView(dd, grp.Key);
                if (view == null)
                {
                    foreach (var p in grp)
                        Logger.Warn($"[DimPos] View '{grp.Key}' not found (id='{p.Id}', key='{p.Key.Value}').");
                    missed += grp.Count();
                    continue;
                }

                // enumerate once per view
                var dimsInView = EnumerateDisplayDimensionsFromAnnotations(view);
                if (dimsInView.Count == 0)
                    dimsInView = EnumerateDisplayDimensionsLegacy(view);

                foreach (var p in grp)
                {
                    var target = FindDisplayDimensionSmart(dimsInView, p);
                    if (target == null)
                    {
                        Logger.Warn($"[DimPos] No match in view '{grp.Key}' for key='{p.Key.Value}' (id='{p.Id}').");
                        if (DebugDumpOnMiss) DumpViewDimensions(grp.Key, dimsInView);
                        missed++;
                        continue;
                    }

                    var ann = target.GetAnnotation() as Annotation;
                    if (ann == null)
                    {
                        Logger.Warn($"[DimPos] DisplayDimension has no Annotation (view='{grp.Key}', key='{p.Key.Value}').");
                        missed++;
                        continue;
                    }

                    double x_m = MmToMeters(p.PositionMm[0]);
                    double y_m = MmToMeters(p.PositionMm[1]);

                    try
                    {
                        // Move twice to counter post-solve nudge
                        ann.SetPosition2(x_m, y_m, 0.0);
                        ann.SetPosition2(x_m, y_m, 0.0);

                        // NEW: center all non-angle dimensions (CenterText = true)
                        try
                        {
                            if (TryGetDimensionNumeric(target, out _, out var unit) &&
                                unit != UnitKind.Degree)
                            {
                                target.CenterText = true;
                            }
                        }
                        catch (Exception exCenter)
                        {
                            Logger.Warn($"[DimPos] CenterText failed (view='{grp.Key}', key='{p.Key.Value}'): {exCenter.Message}");
                        }

                        applied++;
                        Logger.Info($"[DimPos] Moved '{p.Key.Value}' in '{grp.Key}' → ({p.PositionMm[0]:F2},{p.PositionMm[1]:F2}) mm.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[DimPos] SetPosition2 failed (view='{grp.Key}', key='{p.Key.Value}', x={x_m:F4}m, y={y_m:F4}m): {ex.Message}");
                        missed++;
                    }
                }
            }

            try { _ds.Rebuild(); } catch { /* best effort */ }

            Logger.Success($"[DimPos] Applied={applied}, Missed={missed}.");
        }

        // --------------------------- Plan DTO ---------------------------

        public sealed class Plan
        {
            public string Id { get; init; } = Guid.NewGuid().ToString("N");
            public string View { get; init; } = "Front";           // "Front","Side","Detail","Section"
            public DimensionKey Key { get; init; } = DimensionKey.From("TL");
            public double[] PositionMm { get; init; } = new[] { 0.0, 0.0 };
            public Quantity Nominal { get; init; } = Quantity.MmOf(0m); // Unit-aware nominal

            public static Plan From(dynamic d) => new()
            {
                Id = d.Id,
                View = d.View,
                Key = d.Key,
                PositionMm = d.PositionMm,
                Nominal = d.Nominal
            };
        }

        // --------------------------- Finders ---------------------------

        private View? FindView(DrawingDoc dd, string logicalViewName)
        {
            try
            {
                var actual = _nameMap.TryGetValue(logicalViewName, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                    ? mapped : logicalViewName;

                var v = dd.IGetFirstView();
                if (v == null) return null;

                v = v.IGetNextView(); // skip sheet
                int guard = 0;

                while (v != null && guard++ < 1024)
                {
                    var vn = SafeName(v);
                    if (!string.IsNullOrWhiteSpace(vn) &&
                        string.Equals(vn, actual, StringComparison.OrdinalIgnoreCase))
                        return v;
                    v = v.IGetNextView();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DimPos] FindView('{logicalViewName}') failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>Preferred enumeration via annotations.</summary>
        private static IReadOnlyList<DisplayDimension> EnumerateDisplayDimensionsFromAnnotations(View v)
        {
            var list = new List<DisplayDimension>(64);
            if (v == null) return list;

            try
            {
                var raw = v.GetAnnotations();
                if (raw is object[] arr)
                {
                    foreach (var o in arr)
                    {
                        if (o is not Annotation a) continue;
                        var spec = a.GetSpecificAnnotation();
                        if (spec is DisplayDimension dd)
                            list.Add(dd);
                    }
                }
            }
            catch { /* best-effort */ }

            return list;
        }

        /// <summary>Legacy enumeration using View.GetDisplayDimensions().</summary>
        private static IReadOnlyList<DisplayDimension> EnumerateDisplayDimensionsLegacy(View v)
        {
            var list = new List<DisplayDimension>(64);
            if (v == null) return list;

            try
            {
                var raw = v.GetDisplayDimensions();
                if (raw is object[] arr)
                {
                    foreach (var o in arr)
                        if (o is DisplayDimension dd) list.Add(dd);
                }
            }
            catch { /* best-effort */ }

            return list;
        }

        // --------------------------- Matching (token → nearest) ---------------------------

        private static DisplayDimension? FindDisplayDimensionSmart(IReadOnlyList<DisplayDimension> inView, Plan p)
        {
            if (inView == null || inView.Count == 0) return null;

            // A) exact token before '@'
            var exactMatches = new List<DisplayDimension>();
            for (int i = 0; i < inView.Count; i++)
            {
                var token = GetDimToken(inView[i]);
                if (token != null && token.Equals(p.Key.Value, StringComparison.OrdinalIgnoreCase))
                    exactMatches.Add(inView[i]);
            }
            if (exactMatches.Count == 1) return exactMatches[0];
            if (exactMatches.Count > 1) return PickNearest(exactMatches, p.PositionMm);

            // B) relaxed name (prefix/contains)
            var relaxed = new List<DisplayDimension>();
            for (int i = 0; i < inView.Count; i++)
            {
                var full = GetDimFullName(inView[i]);
                if (string.IsNullOrWhiteSpace(full)) continue;

                if (full.StartsWith(p.Key.Value, StringComparison.OrdinalIgnoreCase) ||
                    full.IndexOf(p.Key.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                    relaxed.Add(inView[i]);
            }
            if (relaxed.Count == 1) return relaxed[0];
            if (relaxed.Count > 1) return PickNearest(relaxed, p.PositionMm);

            // C) numeric fallback
            if (TryGetTargetNumeric(p, out var targetVal, out var targetUnit))
            {
                const double epsMm = 0.0005;
                const double epsDeg = 1e-4;
                var numeric = new List<DisplayDimension>();
                for (int i = 0; i < inView.Count; i++)
                {
                    if (TryGetDimensionNumeric(inView[i], out var val, out var unit))
                    {
                        if (targetUnit == UnitKind.Millimeter && unit == UnitKind.Millimeter && Math.Abs(val - targetVal) <= epsMm)
                            numeric.Add(inView[i]);
                        else if (targetUnit == UnitKind.Degree && unit == UnitKind.Degree && Math.Abs(val - targetVal) <= epsDeg)
                            numeric.Add(inView[i]);
                    }
                }
                if (numeric.Count == 1) return numeric[0];
                if (numeric.Count > 1) return PickNearest(numeric, p.PositionMm);
            }

            return null;
        }

        private static DisplayDimension PickNearest(IReadOnlyList<DisplayDimension> candidates, double[] planMm)
        {
            DisplayDimension best = candidates[0];
            double bestD2 = double.PositiveInfinity;

            foreach (var dd in candidates)
            {
                try
                {
                    var ann = dd.GetAnnotation() as Annotation;
                    if (ann == null) continue;

                    // Get current position via Annotation.GetPosition() → object[] {x,y,z} in meters
                    double x = 0, y = 0;
                    var raw = ann.GetPosition() as object[];
                    if (raw != null && raw.Length >= 2 && raw[0] is double dx && raw[1] is double dy)
                    {
                        x = dx; y = dy;
                    }
                    else
                    {
                        // if not available, treat as far
                        continue;
                    }

                    double dxm = (x * 1000.0) - planMm[0];
                    double dym = (y * 1000.0) - planMm[1];
                    double d2 = dxm * dxm + dym * dym;

                    if (d2 < bestD2) { bestD2 = d2; best = dd; }
                }
                catch { /* ignore */ }
            }
            return best;
        }

        private static string? GetDimToken(DisplayDimension dd)
        {
            try
            {
                var swDim = dd?.GetDimension() as SwDimension;
                var full = swDim?.FullName ?? swDim?.Name;
                if (string.IsNullOrWhiteSpace(full)) return null;
                var idx = full.IndexOf('@');
                return idx > 0 ? full.Substring(0, idx) : full; // token before '@'
            }
            catch { return null; }
        }

        private static string? GetDimFullName(DisplayDimension dd)
        {
            try
            {
                var swDim = dd?.GetDimension() as SwDimension;
                return swDim?.FullName ?? swDim?.Name;
            }
            catch { return null; }
        }

        // --------------------------- Numeric helpers ---------------------------

        private static bool TryGetDimensionNumeric(DisplayDimension dd, out double value, out UnitKind unit)
        {
            value = 0; unit = UnitKind.Millimeter;
            try
            {
                var dim = dd?.GetDimension() as SwDimension;
                if (dim == null) return false;

                int type = dim.GetType();     // 1 = angular, 2 = linear
                double raw = dim.SystemValue; // SI units

                if (!double.IsFinite(raw)) return false;

                if (type == 1)
                {
                    unit = UnitKind.Degree;
                    value = raw * (180.0 / Math.PI);
                    return true;
                }

                unit = UnitKind.Millimeter;
                value = raw * 1000.0;
                return true;
            }
            catch { return false; }
        }

        private static bool TryGetTargetNumeric(Plan p, out double value, out UnitKind unit)
        {
            value = 0; unit = UnitKind.Millimeter;

            if (p.Nominal.Unit == UnitKind.Millimeter)
            {
                unit = UnitKind.Millimeter;
                value = (double)p.Nominal.Value;
                return true;
            }
            if (p.Nominal.Unit == UnitKind.Degree)
            {
                unit = UnitKind.Degree;
                value = (double)p.Nominal.Value;
                return true;
            }

            return false;
        }

        // --------------------------- debug dump ---------------------------

        private static void DumpViewDimensions(string viewName, IReadOnlyList<DisplayDimension> list)
        {
            try
            {
                Logger.Info($"[DimPos] View '{viewName}' dimension dump ({list.Count} items):");
                foreach (var dd in list)
                {
                    var name = GetDimFullName(dd) ?? "(no-name)";
                    string valStr = "?";
                    if (TryGetDimensionNumeric(dd, out var v, out var u))
                        valStr = u == UnitKind.Millimeter ? $"{v:F4} mm" : $"{v:F4} deg";
                    Logger.Info($"    • {name}  =  {valStr}");
                }
            }
            catch { /* ignore */ }
        }

        // --------------------------- misc ---------------------------

        private static string SafeName(View v)
        {
            try { return v?.Name ?? "(null)"; }
            catch { return "(ex)"; }
        }

        private static double MmToMeters(double mm) => mm / 1000.0;
    }
}

// DrawingAutomation/Views/AnnotationPositionService.cs
using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

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
    ///
    /// PERFORMANCE NOTES:
    /// - Enumerate each view only once.
    /// - Cache parsed dimension metadata per view.
    /// - Avoid repeated COM calls during matching.
    /// - Rebuild once at the end.
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

            var grouped = planned
                .Where(p => p != null && p.PositionMm != null && p.PositionMm.Length >= 2)
                .GroupBy(p => p.View, StringComparer.OrdinalIgnoreCase);

            int applied = 0;
            int missed = 0;

            foreach (var grp in grouped)
            {
                var view = FindView(dd, grp.Key);
                if (view == null)
                {
                    int missCount = 0;
                    foreach (var p in grp)
                    {
                        Logger.Warn($"[DimPos] View '{grp.Key}' not found (id='{p.Id}', key='{p.Key.Value}').");
                        missCount++;
                    }

                    missed += missCount;
                    continue;
                }

                var dimsInView = ReadDisplayDimensionInfos(view);
                if (dimsInView.Count == 0)
                {
                    int missCount = 0;
                    foreach (var p in grp)
                    {
                        Logger.Warn($"[DimPos] No display dimensions found in view '{grp.Key}' (id='{p.Id}', key='{p.Key.Value}').");
                        missCount++;
                    }

                    missed += missCount;
                    continue;
                }

                foreach (var p in grp)
                {
                    var target = FindDisplayDimensionSmart(dimsInView, p);
                    if (target == null)
                    {
                        Logger.Warn($"[DimPos] No match in view '{grp.Key}' for key='{p.Key.Value}' (id='{p.Id}').");
                        if (DebugDumpOnMiss)
                            DumpViewDimensions(grp.Key, dimsInView);
                        missed++;
                        continue;
                    }

                    if (target.Annotation == null)
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
                        target.Annotation.SetPosition2(x_m, y_m, 0.0);
                        target.Annotation.SetPosition2(x_m, y_m, 0.0);

                        // Center all non-angle dimensions
                        try
                        {
                            if (target.HasNumericValue && target.Unit != UnitKind.Degree)
                                target.DisplayDimension.CenterText = true;
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

            try { _ds.Rebuild(redraw: false); } catch { }

            Logger.Success($"[DimPos] Applied={applied}, Missed={missed}.");
        }

        // --------------------------- Plan DTO ---------------------------

        public sealed class Plan
        {
            public string Id { get; init; } = Guid.NewGuid().ToString("N");
            public string View { get; init; } = "Front";
            public DimensionKey Key { get; init; } = DimensionKey.From("TL");
            public double[] PositionMm { get; init; } = new[] { 0.0, 0.0 };
            public Quantity Nominal { get; init; } = Quantity.MmOf(0m);

            public static Plan From(dynamic d) => new()
            {
                Id = d.Id,
                View = d.View,
                Key = d.Key,
                PositionMm = d.PositionMm,
                Nominal = d.Nominal
            };
        }

        // --------------------------- Cached view dimension info ---------------------------

        private sealed class DisplayDimensionInfo
        {
            public required DisplayDimension DisplayDimension { get; init; }
            public Annotation? Annotation { get; init; }
            public string Token { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public double PositionXmm { get; init; }
            public double PositionYmm { get; init; }
            public bool HasPosition { get; init; }
            public bool HasNumericValue { get; init; }
            public double NumericValue { get; init; }
            public UnitKind Unit { get; init; } = UnitKind.Millimeter;
        }

        // --------------------------- Finders ---------------------------

        private View? FindView(DrawingDoc dd, string logicalViewName)
        {
            try
            {
                var actual = _nameMap.TryGetValue(logicalViewName, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                    ? mapped
                    : logicalViewName;

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

        private static IReadOnlyList<DisplayDimensionInfo> ReadDisplayDimensionInfos(View v)
        {
            var dims = EnumerateDisplayDimensionsFromAnnotations(v);
            if (dims.Count == 0)
                dims = EnumerateDisplayDimensionsLegacy(v);

            if (dims.Count == 0)
                return Array.Empty<DisplayDimensionInfo>();

            var result = new List<DisplayDimensionInfo>(dims.Count);

            for (int i = 0; i < dims.Count; i++)
            {
                var dd = dims[i];
                if (dd == null) continue;

                string token = string.Empty;
                string fullName = string.Empty;
                bool hasNumeric = false;
                double numericValue = 0.0;
                UnitKind unit = UnitKind.Millimeter;
                Annotation? ann = null;
                bool hasPos = false;
                double posXmm = 0.0;
                double posYmm = 0.0;

                try
                {
                    var swDim = dd.GetDimension() as SwDimension;
                    if (swDim != null)
                    {
                        fullName = swDim.FullName ?? swDim.Name ?? string.Empty;
                        token = ExtractToken(fullName);

                        if (TryGetDimensionNumeric(swDim, out var val, out var uk))
                        {
                            hasNumeric = true;
                            numericValue = val;
                            unit = uk;
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                try
                {
                    ann = dd.GetAnnotation() as Annotation;
                    if (ann != null)
                    {
                        var raw = ann.GetPosition() as object[];
                        if (raw != null && raw.Length >= 2 &&
                            raw[0] is double dx &&
                            raw[1] is double dy)
                        {
                            posXmm = dx * 1000.0;
                            posYmm = dy * 1000.0;
                            hasPos = true;
                        }
                    }
                }
                catch
                {
                    // ignore
                }

                result.Add(new DisplayDimensionInfo
                {
                    DisplayDimension = dd,
                    Annotation = ann,
                    Token = token,
                    FullName = fullName,
                    PositionXmm = posXmm,
                    PositionYmm = posYmm,
                    HasPosition = hasPos,
                    HasNumericValue = hasNumeric,
                    NumericValue = numericValue,
                    Unit = unit
                });
            }

            return result;
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
            catch
            {
                // best effort
            }

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
                    {
                        if (o is DisplayDimension dd)
                            list.Add(dd);
                    }
                }
            }
            catch
            {
                // best effort
            }

            return list;
        }

        // --------------------------- Matching (token → nearest) ---------------------------

        private static DisplayDimensionInfo? FindDisplayDimensionSmart(IReadOnlyList<DisplayDimensionInfo> inView, Plan p)
        {
            if (inView == null || inView.Count == 0)
                return null;

            // A) exact token before '@'
            var exactMatches = new List<DisplayDimensionInfo>();
            for (int i = 0; i < inView.Count; i++)
            {
                var info = inView[i];
                if (!string.IsNullOrWhiteSpace(info.Token) &&
                    info.Token.Equals(p.Key.Value, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatches.Add(info);
                }
            }

            if (exactMatches.Count == 1) return exactMatches[0];
            if (exactMatches.Count > 1) return PickNearest(exactMatches, p.PositionMm);

            // B) relaxed full name
            var relaxedMatches = new List<DisplayDimensionInfo>();
            for (int i = 0; i < inView.Count; i++)
            {
                var full = inView[i].FullName;
                if (string.IsNullOrWhiteSpace(full)) continue;

                if (full.StartsWith(p.Key.Value, StringComparison.OrdinalIgnoreCase) ||
                    full.IndexOf(p.Key.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    relaxedMatches.Add(inView[i]);
                }
            }

            if (relaxedMatches.Count == 1) return relaxedMatches[0];
            if (relaxedMatches.Count > 1) return PickNearest(relaxedMatches, p.PositionMm);

            // C) numeric fallback
            if (TryGetTargetNumeric(p, out var targetVal, out var targetUnit))
            {
                const double epsMm = 0.0005;
                const double epsDeg = 1e-4;

                var numericMatches = new List<DisplayDimensionInfo>();
                for (int i = 0; i < inView.Count; i++)
                {
                    var info = inView[i];
                    if (!info.HasNumericValue) continue;

                    if (targetUnit == UnitKind.Millimeter &&
                        info.Unit == UnitKind.Millimeter &&
                        Math.Abs(info.NumericValue - targetVal) <= epsMm)
                    {
                        numericMatches.Add(info);
                    }
                    else if (targetUnit == UnitKind.Degree &&
                             info.Unit == UnitKind.Degree &&
                             Math.Abs(info.NumericValue - targetVal) <= epsDeg)
                    {
                        numericMatches.Add(info);
                    }
                }

                if (numericMatches.Count == 1) return numericMatches[0];
                if (numericMatches.Count > 1) return PickNearest(numericMatches, p.PositionMm);
            }

            return null;
        }

        private static DisplayDimensionInfo PickNearest(IReadOnlyList<DisplayDimensionInfo> candidates, double[] planMm)
        {
            var best = candidates[0];
            double bestD2 = double.PositiveInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (!c.HasPosition) continue;

                double dx = c.PositionXmm - planMm[0];
                double dy = c.PositionYmm - planMm[1];
                double d2 = dx * dx + dy * dy;

                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = c;
                }
            }

            return best;
        }

        private static string ExtractToken(string? full)
        {
            if (string.IsNullOrWhiteSpace(full))
                return string.Empty;

            var idx = full.IndexOf('@');
            return idx > 0 ? full[..idx] : full;
        }

        // --------------------------- Numeric helpers ---------------------------

        private static bool TryGetDimensionNumeric(SwDimension dim, out double value, out UnitKind unit)
        {
            value = 0;
            unit = UnitKind.Millimeter;

            try
            {
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
            catch
            {
                return false;
            }
        }

        private static bool TryGetTargetNumeric(Plan p, out double value, out UnitKind unit)
        {
            value = 0;
            unit = UnitKind.Millimeter;

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

        private static void DumpViewDimensions(string viewName, IReadOnlyList<DisplayDimensionInfo> list)
        {
            try
            {
                Logger.Info($"[DimPos] View '{viewName}' dimension dump ({list.Count} items):");
                foreach (var info in list)
                {
                    string valStr = "?";
                    if (info.HasNumericValue)
                        valStr = info.Unit == UnitKind.Millimeter
                            ? $"{info.NumericValue:F4} mm"
                            : $"{info.NumericValue:F4} deg";

                    Logger.Info($"    • {info.FullName}  =  {valStr}");
                }
            }
            catch
            {
                // ignore
            }
        }

        // --------------------------- misc ---------------------------

        private static string SafeName(View v)
        {
            try { return v?.Name ?? "(null)"; }
            catch { return "(ex)"; }
        }

        private static string SafeNameStatic(View v)
        {
            try { return v?.Name ?? "(null)"; }
            catch { return "(ex)"; }
        }

        private static double MmToMeters(double mm) => mm / 1000.0;

        public object? InsertModelAnnotationsInView(
            string logicalViewName,
            swInsertAnnotation_e types = swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing,
            int source = 0,
            bool allTypes = true,
            bool addToAllViews = false,
            bool includeItemsFromHiddenFeatures = false,
            bool includeItemsFromHiddenSketches = false)
        {
            if (string.IsNullOrWhiteSpace(logicalViewName))
                throw new ArgumentException("View name is required.", nameof(logicalViewName));

            var dd = _ds.Drawing as DrawingDoc;
            var model = _ds.Model as ModelDoc2;
            if (dd is null || model is null)
            {
                Logger.Warn("[AnnIns] Skipped: drawing or model is null.");
                return null;
            }

            var view = FindView(dd, logicalViewName);
            if (view == null)
            {
                Logger.Warn($"[AnnIns] View '{logicalViewName}' not found. No annotations inserted.");
                return null;
            }

            var actualViewName = SafeName(view);

            try
            {
                bool selected = model.Extension.SelectByID2(
                    actualViewName,
                    "DRAWINGVIEW",
                    0, 0, 0,
                    false,
                    0,
                    null,
                    0);

                if (!selected)
                {
                    Logger.Warn($"[AnnIns] Could not select view '{actualViewName}'. InsertModelAnnotations3 may target the wrong view.");
                }

                object? inserted = dd.InsertModelAnnotations3(
                    source,
                    (int)types,
                    allTypes,
                    addToAllViews,
                    includeItemsFromHiddenFeatures,
                    includeItemsFromHiddenSketches);

                Logger.Info($"[AnnIns] InsertModelAnnotations3 in view '{actualViewName}' (types={types}, source={source}).");
                return inserted;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[AnnIns] InsertModelAnnotationsInView failed (view='{actualViewName}'): {ex.Message}");
                return null;
            }
        }

        public object? InsertMarkedAnnotationsStrictInView(
            string logicalViewName,
            int source = 0,
            bool includeItemsFromHiddenFeatures = false,
            bool includeItemsFromHiddenSketches = false)
        {
            if (string.IsNullOrWhiteSpace(logicalViewName))
                throw new ArgumentException("View name is required.", nameof(logicalViewName));

            var dd = _ds.Drawing as DrawingDoc;
            var model = _ds.Model as ModelDoc2;
            if (dd is null || model is null)
            {
                Logger.Warn("[AnnIns] Skipped: drawing or model is null.");
                return null;
            }

            var targetView = FindView(dd, logicalViewName);
            if (targetView == null)
            {
                Logger.Warn($"[AnnIns] View '{logicalViewName}' not found. No annotations inserted.");
                return null;
            }

            var actualViewName = SafeName(targetView);

            try
            {
                var before = SnapshotDisplayDimensionsByView(dd);

                try { dd.ActivateView(actualViewName); } catch { }

                model.ClearSelection2(true);

                bool selected = model.Extension.SelectByID2(
                    actualViewName,
                    "DRAWINGVIEW",
                    0, 0, 0,
                    false,
                    0,
                    null,
                    0);

                if (!selected)
                    Logger.Warn($"[AnnIns] Could not select view '{actualViewName}'. InsertModelAnnotations3 may target the wrong view.");

                object? inserted = dd.InsertModelAnnotations3(
                    source,
                    (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing,
                    false,
                    false,
                    includeItemsFromHiddenFeatures,
                    includeItemsFromHiddenSketches);

                var after = SnapshotDisplayDimensionsByView(dd);

                var targetKey = actualViewName;
                int kept = 0;
                int removed = 0;

                foreach (var kv in after)
                {
                    var viewName = kv.Key;
                    var afterSet = kv.Value;

                    before.TryGetValue(viewName, out var beforeSet);

                    foreach (var ddim in afterSet)
                    {
                        if (beforeSet != null && beforeSet.Contains(ddim))
                            continue;

                        if (!string.Equals(viewName, targetKey, StringComparison.OrdinalIgnoreCase))
                        {
                            TryDeleteDisplayDimension(ddim);
                            removed++;
                        }
                        else
                        {
                            kept++;
                        }
                    }
                }

                Logger.Info($"[AnnIns] InsertModelAnnotations3 requested for '{actualViewName}'. Kept={kept}, RemovedOutsideTarget={removed}.");
                try { _ds.Rebuild(redraw: false); } catch { }

                return inserted;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[AnnIns] InsertMarkedAnnotationsStrictInView failed (view='{actualViewName}'): {ex.Message}");
                return null;
            }
        }

        // ---------------- helpers ----------------

        private static Dictionary<string, HashSet<DisplayDimension>> SnapshotDisplayDimensionsByView(DrawingDoc dd)
        {
            var map = new Dictionary<string, HashSet<DisplayDimension>>(StringComparer.OrdinalIgnoreCase);

            View v = dd.IGetFirstView();
            if (v == null) return map;

            v = v.IGetNextView(); // skip sheet

            int guard = 0;
            while (v != null && guard++ < 2048)
            {
                var name = SafeNameStatic(v);

                var set = new HashSet<DisplayDimension>(ReferenceEqualityComparer<DisplayDimension>.Instance);

                foreach (var d in EnumerateDisplayDimensionsFromAnnotations(v))
                    set.Add(d);

                if (set.Count == 0)
                {
                    foreach (var d in EnumerateDisplayDimensionsLegacy(v))
                        set.Add(d);
                }

                map[name] = set;
                v = v.IGetNextView();
            }

            return map;
        }

        private void TryDeleteDisplayDimension(DisplayDimension ddim)
        {
            try
            {
                var model = _ds.Model as ModelDoc2;
                if (model == null) return;

                var ann = ddim.GetAnnotation() as Annotation;
                if (ann == null) return;

                model.ClearSelection2(true);

                bool ok = ann.Select3(false, null);
                if (!ok) return;

                model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch
            {
                // best effort
            }
        }

        // Reference-based HashSet comparer for COM objects
        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new();

            public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
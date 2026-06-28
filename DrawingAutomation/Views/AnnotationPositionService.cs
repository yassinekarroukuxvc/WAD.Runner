
using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DrawingAutomation.SolidWorks;


using SwDimension = SolidWorks.Interop.sldworks.Dimension;

namespace WAD.Runner.DrawingAutomation.Views
{


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
                    var target = FindDisplayDimensionSmart(dimsInView, p, wedge);
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


                        bool movedTextPoint = TrySetDisplayDimensionTextPoint(target.DisplayDimension, x_m, y_m, 0.0);


                        target.Annotation.SetPosition2(x_m, y_m, 0.0);
                        target.Annotation.SetPosition2(x_m, y_m, 0.0);


                        try
                        {
                            if (target.HasNumericValue)
                                target.DisplayDimension.CenterText = true;
                        }
                        catch (Exception exCenter)
                        {
                            Logger.Warn($"[DimPos] CenterText failed (view='{grp.Key}', key='{p.Key.Value}', full='{target.FullName}'): {exCenter.Message}");
                        }

                        applied++;
                        Logger.Info($"[DimPos] Moved '{p.Key.Value}' in '{grp.Key}' full='{target.FullName}' → ({p.PositionMm[0]:F2},{p.PositionMm[1]:F2}) mm. TextPoint={movedTextPoint}");
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


        private View? FindView(DrawingDoc dd, string logicalViewName)
        {
            try
            {
                var actual = _nameMap.TryGetValue(logicalViewName, out var mapped) && !string.IsNullOrWhiteSpace(mapped)
                    ? mapped
                    : logicalViewName;

                var v = dd.IGetFirstView();
                if (v == null) return null;

                v = v.IGetNextView();
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

            }

            return list;
        }


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

            }

            return list;
        }


        private static DisplayDimensionInfo? FindDisplayDimensionSmart(IReadOnlyList<DisplayDimensionInfo> inView, Plan p, WedgeData wedge)
        {
            if (inView == null || inView.Count == 0)
                return null;


            var expectedMatches = new List<DisplayDimensionInfo>();
            foreach (var expectedFullName in BuildExpectedFullNames(wedge, p))
            {
                for (int i = 0; i < inView.Count; i++)
                {
                    var info = inView[i];
                    if (FullNameMatchesExpected(info.FullName, expectedFullName) &&
                        !expectedMatches.Any(x => string.Equals(x.FullName, info.FullName, StringComparison.OrdinalIgnoreCase)))
                    {
                        expectedMatches.Add(info);
                    }
                }

                if (expectedMatches.Count == 1)
                    return expectedMatches[0];

                if (expectedMatches.Count > 1)
                    return PickNearest(expectedMatches, p.PositionMm);
            }


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


            var strictFullNameMatches = new List<DisplayDimensionInfo>();
            for (int i = 0; i < inView.Count; i++)
            {
                var full = inView[i].FullName;
                if (string.IsNullOrWhiteSpace(full)) continue;

                if (FullNameStartsWithExactDimensionToken(full, p.Key.Value))
                    strictFullNameMatches.Add(inView[i]);
            }

            if (strictFullNameMatches.Count == 1) return strictFullNameMatches[0];
            if (strictFullNameMatches.Count > 1) return PickNearest(strictFullNameMatches, p.PositionMm);


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

        private static IEnumerable<string> BuildExpectedFullNames(WedgeData wedge, Plan p)
        {
            if (p == null || p.Key == null || string.IsNullOrWhiteSpace(p.Key.Value))
                yield break;

            var key = p.Key.Value.Trim();
            var view = p.View?.Trim() ?? string.Empty;

            bool is180 = Is180DegRev(wedge);
            string frontSketch = is180 ? "ANNOT_180_DEG_REV_FRONT_sketch" : "ANNOT_STD_FRONT_sketch";
            string topSketch = is180 ? "ANNOT_180_DEG_REV_TOP_sketch" : "ANNOT_STD_TOP_sketch";
            string frBrSketch = is180 ? "ANNOT_FR_BR_180_DEG_REV_FRONT_sketch" : "ANNOT_FR_BR_STD_FRONT_sketch";

            if (view.Equals("Section", StringComparison.OrdinalIgnoreCase))
            {
                if (key.StartsWith("FR_", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("BR_", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{key}@{frBrSketch}";
                    yield break;
                }


                yield return $"{key}@{frontSketch}";


                if (is180 && (key.Equals("G", StringComparison.OrdinalIgnoreCase) ||
                              key.Equals("CGR", StringComparison.OrdinalIgnoreCase) ||
                              key.Equals("CGD", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return $"{key}@ANNOT_180_DEG_REV_FRONT_FRONT_sketch";
                }

                yield break;
            }

            if (view.Equals("Top", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{key}@{topSketch}";
                yield break;
            }

            if (view.Equals("Side", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{key}@{frontSketch}";
                yield break;
            }

            if (view.Equals("Front", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("VR", StringComparison.OrdinalIgnoreCase))
                    yield return "VR@ANNOT_LEFT_sketch";
                else if (!key.Equals("K", StringComparison.OrdinalIgnoreCase))
                    yield return $"{key}@{frontSketch}";

                yield break;
            }

            if (view.Equals("Detail", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("W", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("W2", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("ISA", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("VW", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("VR", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{key}@ANNOT_LEFT_sketch";
                }
                else
                {
                    yield return $"{key}@ANNOT_FOOT_OPTIONS_LEFT_sketch";
                }
            }
        }

        private static bool FullNameMatchesExpected(string actualFullName, string expectedFullName)
        {
            var actual = NormalizeFullNameForCompare(actualFullName);
            var expected = NormalizeFullNameForCompare(expectedFullName);

            return !string.IsNullOrWhiteSpace(actual) &&
                   !string.IsNullOrWhiteSpace(expected) &&
                   actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool FullNameStartsWithExactDimensionToken(string actualFullName, string key)
        {
            if (string.IsNullOrWhiteSpace(actualFullName) || string.IsNullOrWhiteSpace(key))
                return false;

            var actual = actualFullName.Trim().Trim('"');
            var wanted = key.Trim();


            if (!actual.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                return false;

            if (actual.Length == wanted.Length)
                return true;

            char next = actual[wanted.Length];
            return next == '@' || next == '<' || next == ' ' || next == '\t';
        }

        private static string NormalizeFullNameForCompare(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return string.Empty;

            var s = fullName.Trim().Trim('"');

            var idx = s.IndexOf('<');
            if (idx >= 0)
                s = s[..idx];

            var parts = s.Split('@');
            if (parts.Length >= 2)
                s = parts[0] + "@" + parts[1];

            s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return s.ToUpperInvariant();
        }

        private static bool Is180DegRev(WedgeData wedge)
        {
            var wedType = GetPropLoose(wedge, "Wed-Type")
                       ?? GetPropLoose(wedge, "Wed_Type")
                       ?? GetPropLoose(wedge, "wedge_type");

            var s = (wedType ?? string.Empty).Trim().ToUpperInvariant();
            return s.Contains("180") || s.Contains("REV");
        }

        private static string? GetPropLoose(WedgeData wedge, string key)
        {
            if (wedge?.Properties == null || string.IsNullOrWhiteSpace(key))
                return null;

            if (wedge.Properties.TryGetValue(key, out var direct) && !string.IsNullOrWhiteSpace(direct))
                return direct.Trim();

            foreach (var kv in wedge.Properties)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(kv.Value))
                {
                    return kv.Value.Trim();
                }
            }

            return null;
        }

        private static bool TrySetDisplayDimensionTextPoint(DisplayDimension displayDimension, double x, double y, double z)
        {
            if (displayDimension == null)
                return false;

            try
            {
                dynamic dd = displayDimension;
                dd.SetTextPoint2(x, y, z);
                return true;
            }
            catch
            {

            }

            try
            {
                dynamic dd = displayDimension;
                dd.SetTextPoint(x, y, z);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractToken(string? full)
        {
            if (string.IsNullOrWhiteSpace(full))
                return string.Empty;

            var idx = full.IndexOf('@');
            return idx > 0 ? full[..idx] : full;
        }


        private static bool TryGetDimensionNumeric(SwDimension dim, out double value, out UnitKind unit)
        {
            value = 0;
            unit = UnitKind.Millimeter;

            try
            {
                if (dim == null) return false;

                int type = dim.GetType();
                double raw = dim.SystemValue;

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

            }
        }


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


        private static Dictionary<string, HashSet<DisplayDimension>> SnapshotDisplayDimensionsByView(DrawingDoc dd)
        {
            var map = new Dictionary<string, HashSet<DisplayDimension>>(StringComparer.OrdinalIgnoreCase);

            View v = dd.IGetFirstView();
            if (v == null) return map;

            v = v.IGetNextView();

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

            }
        }


        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new();

            public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

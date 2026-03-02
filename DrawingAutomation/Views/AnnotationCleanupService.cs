using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;                           // Logger
using WAD.Runner.DataManagement.Domain.Drawing;         // DrawingData
using WAD.Runner.DataManagement.Domain.Planning;        // LayoutContext, LayoutMath, DimensionSpec
using WAD.Runner.DrawingAutomation.SolidWorks;          // DrawingService
using WAD.Runner.DataManagement.Infrastructure.Mapping; // DimensionKeyPolicy

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Centralized helpers for cleaning up annotations on a drawing,
/// e.g. removing dimensions whose value is 0 based on planning data.
///
/// Notes:
/// - "FullName" matching in drawings is often NOT the same as your planning/annotation key name.
/// - RemoveDimensionsByFullNamesInView(...) supports normalization and "starts-with" matching
///   to handle suffixes like "@<PartName>" that SolidWorks appends.
/// - Additionally: when 0 deletions happen, this file now auto-dumps the existing dims in that view
///   so you can immediately see what names SolidWorks is using (fixes the “Front deleted 0 with no clue” problem).
/// </summary>
public static class AnnotationCleanupService
{
    // =========================
    // 0) DIAGNOSTICS
    // =========================

    /// <summary>
    /// Dumps all drawing DisplayDimensions (Name + FullName) found in a given logical view.
    /// Use this to verify what SolidWorks is actually returning before you try to delete.
    /// </summary>
    public static void DumpDisplayDimensionNames(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        string logicalViewName,
        int max = 250)
    {
        if (ds?.Drawing is not DrawingDoc) return;

        var view = FindView(ds, logicalViewName, nameMap);
        if (view == null)
        {
            Logger.Warn($"[DumpDims] View '{logicalViewName}' not found.");
            return;
        }

        object dispDimsObj;
        try
        {
            dispDimsObj = view.GetDisplayDimensions();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[DumpDims] GetDisplayDimensions failed for '{logicalViewName}': {ex.Message}");
            return;
        }

        if (dispDimsObj is not object[] arr || arr.Length == 0)
        {
            Logger.Info($"[DumpDims] View '{logicalViewName}' has 0 display dimensions.");
            return;
        }

        Logger.Info($"[DumpDims] View '{logicalViewName}' display dimensions = {arr.Length}");

        int i = 0;
        foreach (var obj in arr)
        {
            if (i++ >= max) break;
            if (obj is not DisplayDimension dd) continue;

            string name = "";
            string full = "";

            try
            {
                var dim = dd.GetDimension() as Dimension;
                if (dim != null)
                {
                    name = dim.Name ?? "";
                    full = dim.FullName ?? "";
                }
            }
            catch
            {
                // ignore
            }

            Logger.Info($"[DumpDims]  - Name='{name}' FullName='{full}'");
        }
    }

    // =========================
    // 1) ZERO VALUE CLEANUP
    // =========================

    /// <summary>
    /// Uses planning data (LayoutContext + DimensionRules output) to identify
    /// which dimension keys have a value of 0 (mm or deg), then removes any
    /// corresponding annotations from all logical views.
    ///
    /// - Length keys → compared in mm via LayoutMath.Dmm
    /// - Angle keys  → compared in deg via LayoutMath.Ddeg
    ///
    /// We also explicitly include FX/VR/VRR in the key set so they are cleaned
    /// even if no DimensionSpec exists for them.
    /// </summary>
    public static void RemoveZeroDimensionsFromDrawing(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        LayoutContext ctx,
        DrawingData drawingData,
        IEnumerable<DimensionSpec> dims)
    {
        if (ds?.Model is not ModelDoc2) return;
        if (ds.Drawing is not DrawingDoc) return;
        if (drawingData?.Views == null) return;

        var candidateKeyStrings = dims
            .Select(d => d.Key.ToString())
            .Concat(new[] { "FX", "VR", "VRR" })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var zeroKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyStr in candidateKeyStrings)
        {
            double value;
            try
            {
                value = DimensionKeyPolicy.IsAngle(keyStr)
                    ? LayoutMath.Ddeg(ctx, keyStr)
                    : LayoutMath.Dmm(ctx, keyStr);
            }
            catch
            {
                continue;
            }

            if (Math.Abs(value) < 1e-6)
                zeroKeys.Add(keyStr);
        }

        if (zeroKeys.Count == 0)
        {
            Logger.Info("[ZeroCleanup] No keys with zero value (mm/deg) detected – nothing to delete.");
            return;
        }

        Logger.Info($"[ZeroCleanup] Keys with zero value (mm/deg): {string.Join(", ", zeroKeys)}");

        int totalDeleted = 0;

        foreach (var logicalViewName in drawingData.Views.Keys)
        {
            foreach (var key in zeroKeys)
                totalDeleted += DeleteDimensionAnnotationsForKey(ds, nameMap, logicalViewName, key);
        }

        Logger.Info($"[ZeroCleanup] Total deleted dimension annotations: {totalDeleted}");
    }

    // =========================
    // 2) MANUAL KEY CLEANUP
    // =========================

    /// <summary>
    /// Deletes dimension annotations (DisplayDimensions) in ONE logical view,
    /// matching a list of keys (prefix match on Dimension.FullName up to '@').
    /// </summary>
    public static int RemoveDimensionsByKeysInView(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        string logicalViewName,
        IEnumerable<string> keys)
    {
        if (ds?.Model is not ModelDoc2) return 0;
        if (ds.Drawing is not DrawingDoc) return 0;
        if (string.IsNullOrWhiteSpace(logicalViewName)) return 0;
        if (keys is null) return 0;

        var keyList = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keyList.Count == 0)
        {
            Logger.Info($"[KeyCleanup] No keys provided – nothing to delete in view '{logicalViewName}'.");
            return 0;
        }

        Logger.Info($"[KeyCleanup] Deleting keys in view '{logicalViewName}': {string.Join(", ", keyList)}");

        int deleted = 0;
        foreach (var key in keyList)
            deleted += DeleteDimensionAnnotationsForKey(ds, nameMap, logicalViewName, key);

        Logger.Info($"[KeyCleanup] Deleted dimension annotations in view '{logicalViewName}': {deleted}");
        return deleted;
    }

    // =========================
    // 3) FULL NAME CLEANUP (UPGRADED + AUTO-DUMP ON ZERO MATCH)
    // =========================

    /// <summary>
    /// Deletes dimension annotations (DisplayDimensions) in ONE logical view,
    /// matching a list of FULL names (Dimension.FullName), e.g. "TD@ANNOT_180_DEG_REV_TOP_sketch".
    ///
    /// SolidWorks appends extra suffix segments, commonly:
    /// - "@<PartName>.Part"
    /// - "@1"
    /// - "<1>"
    ///
    /// This method:
    ///  - matches exact
    ///  - matches normalized exact (Key@Sketch)
    ///  - matches normalized starts-with (tolerate suffixes)
    ///
    /// NEW:
    /// - If deleted == 0, it auto-dumps the first N existing dims in that view so you immediately
    ///   see what SolidWorks calls them (critical for debugging your Front mismatch).
    /// </summary>
    public static int RemoveDimensionsByFullNamesInView(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        string logicalViewName,
        IEnumerable<string> fullNames)
    {
        if (ds?.Model is not ModelDoc2) return 0;
        if (ds.Drawing is not DrawingDoc) return 0;
        if (string.IsNullOrWhiteSpace(logicalViewName)) return 0;
        if (fullNames is null) return 0;

        var rawTargets = fullNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();

        var targets = new HashSet<string>(rawTargets, StringComparer.OrdinalIgnoreCase);
        var normalizedTargets = new HashSet<string>(
            rawTargets.Select(NormalizeDimName).Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);

        if (targets.Count == 0)
        {
            Logger.Info($"[FullNameCleanup] No full-names provided – nothing to delete in view '{logicalViewName}'.");
            return 0;
        }

        Logger.Info($"[FullNameCleanup] Deleting full-names in view '{logicalViewName}': {string.Join(", ", targets)}");

        int deleted = 0;

        var view = FindView(ds, logicalViewName, nameMap);
        if (view == null)
        {
            Logger.Warn($"[FullNameCleanup] View '{logicalViewName}' not found.");
            return 0;
        }

        if (ds.Model is not ModelDoc2 model)
            return 0;

        object dispDimsObj;
        try
        {
            dispDimsObj = view.GetDisplayDimensions();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[FullNameCleanup] GetDisplayDimensions failed for view '{logicalViewName}': {ex.Message}");
            return 0;
        }

        if (dispDimsObj is not object[] dispDimsArr || dispDimsArr.Length == 0)
            return 0;

        foreach (var obj in dispDimsArr)
        {
            if (obj is not DisplayDimension dispDim) continue;

            Dimension dim;
            try
            {
                dim = dispDim.GetDimension() as Dimension;
            }
            catch
            {
                continue;
            }

            if (dim == null) continue;

            string dimFullName;
            string dimName;
            try
            {
                dimFullName = dim.FullName ?? string.Empty;
                dimName = dim.Name ?? string.Empty;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(dimFullName))
                continue;

            if (!MatchesFullName(dimFullName, targets, normalizedTargets))
                continue;

            try
            {
                var ann = dispDim.IGetAnnotation() as Annotation;
                if (ann != null)
                {
                    ann.Select2(false, -1);
                    model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    deleted++;

                    Logger.Info($"[FullNameCleanup] Deleted dim Name='{dimName}' FullName='{dimFullName}' in view '{logicalViewName}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[FullNameCleanup] Failed to delete dim '{dimFullName}' in view '{logicalViewName}': {ex.Message}");
            }
        }

        // ✅ NEW: If nothing was deleted, dump what's actually in the view
        if (deleted == 0)
        {
            Logger.Warn($"[FullNameCleanup] No matches in view '{logicalViewName}'. " +
                        "Dumping existing display dimension names (first 120) to help fix rule names:");

            int i = 0;
            foreach (var obj in dispDimsArr)
            {
                if (i++ >= 120) break;
                if (obj is not DisplayDimension dd2) continue;

                try
                {
                    var d2 = dd2.GetDimension() as Dimension;
                    if (d2 == null) continue;

                    var n = d2.Name ?? "";
                    var fn = d2.FullName ?? "";
                    Logger.Warn($"[FullNameCleanup]   existing: Name='{n}' FullName='{fn}'");
                }
                catch
                {
                    // ignore
                }
            }
        }

        Logger.Info($"[FullNameCleanup] Deleted dimension annotations in view '{logicalViewName}': {deleted}");
        return deleted;
    }

    // =========================
    // Internal helpers
    // =========================

    private static bool MatchesFullName(
        string dimFullName,
        HashSet<string> rawTargets,
        HashSet<string> normalizedTargets)
    {
        if (rawTargets.Contains(dimFullName))
            return true;

        var dimNorm = NormalizeDimName(dimFullName);
        if (string.IsNullOrWhiteSpace(dimNorm))
            return false;

        if (normalizedTargets.Contains(dimNorm))
            return true;

        foreach (var t in normalizedTargets)
        {
            if (dimNorm.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes SW dimension name strings to improve matching.
    /// - trims quotes/whitespace
    /// - removes "<...>" suffix
    /// - keeps only first 2 segments when split by '@' (Key@Sketch)
    /// </summary>
    private static string NormalizeDimName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Trim().Trim('"');

        var idx = s.IndexOf('<');
        if (idx >= 0) s = s[..idx];

        var parts = s.Split('@');
        if (parts.Length >= 2)
            s = parts[0] + "@" + parts[1];

        return s;
    }

    /// <summary>
    /// In a given logical view, deletes all DisplayDimensions whose
    /// Dimension.FullName prefix matches the given key (e.g. "FX" in "FX@Sketch1").
    /// </summary>
    private static int DeleteDimensionAnnotationsForKey(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        string logicalViewName,
        string key)
    {
        int deleted = 0;

        var view = FindView(ds, logicalViewName, nameMap);
        if (view == null)
        {
            Logger.Warn($"[ZeroCleanup] View '{logicalViewName}' not found when deleting key '{key}'.");
            return 0;
        }

        if (ds.Model is not ModelDoc2 model)
            return 0;

        object dispDimsObj;
        try
        {
            dispDimsObj = view.GetDisplayDimensions();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ZeroCleanup] GetDisplayDimensions failed for view '{logicalViewName}': {ex.Message}");
            return 0;
        }

        if (dispDimsObj is not object[] dispDimsArr || dispDimsArr.Length == 0)
            return 0;

        foreach (var obj in dispDimsArr)
        {
            if (obj is not DisplayDimension dispDim) continue;

            Dimension dim;
            try
            {
                dim = dispDim.GetDimension() as Dimension;
            }
            catch
            {
                continue;
            }

            if (dim == null) continue;

            string fullName = string.Empty;
            try
            {
                fullName = dim.FullName ?? string.Empty;
            }
            catch
            {
                // ignore
            }

            string prefix = fullName;
            var atIdx = prefix.IndexOf('@');
            if (atIdx >= 0)
                prefix = prefix[..atIdx];

            if (!string.Equals(prefix, key, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var ann = dispDim.IGetAnnotation() as Annotation;
                if (ann != null)
                {
                    ann.Select2(false, -1);
                    model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    deleted++;

                    Logger.Info($"[ZeroCleanup] Deleted dim '{fullName}' in view '{logicalViewName}' for key '{key}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ZeroCleanup] Failed to delete dim '{fullName}' in view '{logicalViewName}' (key='{key}'): {ex.Message}");
            }
        }

        return deleted;
    }

    private static View? FindView(DrawingService ds, string logicalName, IDictionary<string, string> nameMap)
    {
        try
        {
            if (ds?.Drawing is not DrawingDoc dd) return null;

            string actualName = logicalName;
            if (nameMap != null &&
                nameMap.TryGetValue(logicalName, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped))
                actualName = mapped;

            View v = dd.IGetFirstView();
            if (v == null) return null;
            v = v.IGetNextView(); // skip sheet

            int guard = 0;
            while (v != null && guard++ < 512)
            {
                try
                {
                    var vn = v.Name;
                    if (!string.IsNullOrWhiteSpace(vn) &&
                        string.Equals(vn, actualName, StringComparison.OrdinalIgnoreCase))
                        return v;
                }
                catch { }

                v = v.IGetNextView();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Cleanup.FindView('{logicalName}')] failed: {ex.Message}");
        }

        return null;
    }
}
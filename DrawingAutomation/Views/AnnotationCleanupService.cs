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
/// PERFORMANCE NOTES:
/// - Avoid rescanning the same view for each key.
/// - Read DisplayDimensions once per view when possible.
/// - Reduce repeated COM calls and repeated string normalization work.
/// </summary>
public static class AnnotationCleanupService
{
    // =========================
    // Internal DTO
    // =========================

    private sealed class DisplayDimInfo
    {
        public DisplayDimension DisplayDimension { get; init; } = null!;
        public Annotation? Annotation { get; init; }
        public string Name { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;
        public string NormalizedFullName { get; init; } = string.Empty;
    }

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

        var infos = ReadDisplayDimensions(view);
        if (infos.Count == 0)
        {
            Logger.Info($"[DumpDims] View '{logicalViewName}' has 0 display dimensions.");
            return;
        }

        Logger.Info($"[DumpDims] View '{logicalViewName}' display dimensions = {infos.Count}");

        int count = Math.Min(max, infos.Count);
        for (int i = 0; i < count; i++)
        {
            var info = infos[i];
            Logger.Info($"[DumpDims]  - Name='{info.Name}' FullName='{info.FullName}'");
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
        if (ds?.Model is not ModelDoc2 model) return;
        if (ds.Drawing is not DrawingDoc) return;
        if (drawingData?.Views == null) return;
        if (ctx == null) return;
        if (dims == null) return;

        // Only evaluate REAL numeric keys here.
        // Do NOT include VR_MAX / VR_MIN / VRR_MAX / VRR_MIN as standalone keys.
        var candidateKeyStrings = dims
            .Select(d => d.Key.ToString())
            .Concat(new[] { "FX", "VR", "VRR" })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s =>
                !s.Equals("VR_MAX", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("VR_MIN", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("VRR_MAX", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("VRR_MIN", StringComparison.OrdinalIgnoreCase))
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

        
        var prefixesToDelete = new HashSet<string>(zeroKeys, StringComparer.OrdinalIgnoreCase);

        if (zeroKeys.Contains("VR"))
        {
            prefixesToDelete.Add("VR_MAX");
            prefixesToDelete.Add("VR_MIN");
        }

        if (zeroKeys.Contains("VRR"))
        {
            prefixesToDelete.Add("VRR_MAX");
            prefixesToDelete.Add("VRR_MIN");
        }

        Logger.Info($"[ZeroCleanup] Annotation prefixes to delete: {string.Join(", ", prefixesToDelete)}");

        int totalDeleted = 0;

        foreach (var logicalViewName in drawingData.Views.Keys)
        {
            var view = FindView(ds, logicalViewName, nameMap);
            if (view == null)
            {
                Logger.Warn($"[ZeroCleanup] View '{logicalViewName}' not found.");
                continue;
            }

            var infos = ReadDisplayDimensions(view);
            if (infos.Count == 0)
                continue;

            int deletedInView = DeleteDimensionsByPredicate(
                model,
                infos,
                info => prefixesToDelete.Contains(info.Prefix),
                info => $"[ZeroCleanup] Deleted dim '{info.FullName}' in view '{logicalViewName}' for key '{info.Prefix}'.",
                (info, ex) => $"[ZeroCleanup] Failed to delete dim '{info.FullName}' in view '{logicalViewName}' (key='{info.Prefix}'): {ex.Message}");

            totalDeleted += deletedInView;
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
        if (ds?.Model is not ModelDoc2 model) return 0;
        if (ds.Drawing is not DrawingDoc) return 0;
        if (string.IsNullOrWhiteSpace(logicalViewName)) return 0;
        if (keys is null) return 0;

        var keySet = new HashSet<string>(
            keys.Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (keySet.Count == 0)
        {
            Logger.Info($"[KeyCleanup] No keys provided – nothing to delete in view '{logicalViewName}'.");
            return 0;
        }

        Logger.Info($"[KeyCleanup] Deleting keys in view '{logicalViewName}': {string.Join(", ", keySet)}");

        var view = FindView(ds, logicalViewName, nameMap);
        if (view == null)
        {
            Logger.Warn($"[KeyCleanup] View '{logicalViewName}' not found.");
            return 0;
        }

        var infos = ReadDisplayDimensions(view);
        if (infos.Count == 0)
            return 0;

        int deleted = DeleteDimensionsByPredicate(
            model,
            infos,
            info => keySet.Contains(info.Prefix),
            (info) => $"[KeyCleanup] Deleted dim '{info.FullName}' in view '{logicalViewName}' for key '{info.Prefix}'.",
            (info, ex) => $"[KeyCleanup] Failed to delete dim '{info.FullName}' in view '{logicalViewName}' for key '{info.Prefix}': {ex.Message}");

        Logger.Info($"[KeyCleanup] Deleted dimension annotations in view '{logicalViewName}': {deleted}");
        return deleted;
    }

    // =========================
    // 3) FULL NAME CLEANUP
    // =========================

    /// <summary>
    /// Deletes dimension annotations (DisplayDimensions) in ONE logical view,
    /// matching a list of FULL names (Dimension.FullName), e.g. "TD@ANNOT_180_DEG_REV_TOP_sketch".
    ///
    /// SolidWorks appends extra suffix segments, commonly:
    /// - "@&lt;PartName&gt;.Part"
    /// - "@1"
    /// - "&lt;1&gt;"
    ///
    /// This method:
    ///  - matches exact
    ///  - matches normalized exact (Key@Sketch)
    ///  - matches normalized starts-with (tolerate suffixes)
    ///
    /// If deleted == 0, it auto-dumps the first N existing dims in that view so you immediately
    /// see what SolidWorks calls them.
    /// </summary>
    public static int RemoveDimensionsByFullNamesInView(
        DrawingService ds,
        IDictionary<string, string> nameMap,
        string logicalViewName,
        IEnumerable<string> fullNames)
    {
        if (ds?.Model is not ModelDoc2 model) return 0;
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

        var view = FindView(ds, logicalViewName, nameMap);
        if (view == null)
        {
            Logger.Warn($"[FullNameCleanup] View '{logicalViewName}' not found.");
            return 0;
        }

        var infos = ReadDisplayDimensions(view);
        if (infos.Count == 0)
            return 0;

        int deleted = DeleteDimensionsByPredicate(
            model,
            infos,
            info => MatchesFullName(info.FullName, info.NormalizedFullName, targets, normalizedTargets),
            (info) => $"[FullNameCleanup] Deleted dim Name='{info.Name}' FullName='{info.FullName}' in view '{logicalViewName}'.",
            (info, ex) => $"[FullNameCleanup] Failed to delete dim '{info.FullName}' in view '{logicalViewName}': {ex.Message}");

        if (deleted == 0)
        {
            Logger.Warn($"[FullNameCleanup] No matches in view '{logicalViewName}'. Dumping existing display dimension names (first 120) to help fix rule names:");

            int count = Math.Min(120, infos.Count);
            for (int i = 0; i < count; i++)
            {
                var info = infos[i];
                Logger.Warn($"[FullNameCleanup]   existing: Name='{info.Name}' FullName='{info.FullName}'");
            }
        }

        Logger.Info($"[FullNameCleanup] Deleted dimension annotations in view '{logicalViewName}': {deleted}");
        return deleted;
    }

    // =========================
    // Internal helpers
    // =========================

    private static int DeleteDimensionsByPredicate(
        ModelDoc2 model,
        IReadOnlyList<DisplayDimInfo> infos,
        Func<DisplayDimInfo, bool> shouldDelete,
        Func<DisplayDimInfo, string> successMessage,
        Func<DisplayDimInfo, Exception, string> failureMessage)
    {
        int deleted = 0;

        for (int i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            if (!shouldDelete(info))
                continue;

            if (info.Annotation == null)
                continue;

            try
            {
                info.Annotation.Select2(false, -1);
                model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                deleted++;

                Logger.Info(successMessage(info));
            }
            catch (Exception ex)
            {
                Logger.Warn(failureMessage(info, ex));
            }
        }

        return deleted;
    }

    private static List<DisplayDimInfo> ReadDisplayDimensions(View view)
    {
        var result = new List<DisplayDimInfo>();

        object dispDimsObj;
        try
        {
            dispDimsObj = view.GetDisplayDimensions();
        }
        catch
        {
            return result;
        }

        if (dispDimsObj is not object[] arr || arr.Length == 0)
            return result;

        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] is not DisplayDimension dd)
                continue;

            try
            {
                var dim = dd.GetDimension() as Dimension;
                if (dim == null)
                    continue;

                var fullName = dim.FullName ?? string.Empty;
                var name = dim.Name ?? string.Empty;

                if (string.IsNullOrWhiteSpace(fullName))
                    continue;

                var annotation = dd.IGetAnnotation() as Annotation;
                var prefix = ExtractPrefix(fullName);

                result.Add(new DisplayDimInfo
                {
                    DisplayDimension = dd,
                    Annotation = annotation,
                    Name = name,
                    FullName = fullName,
                    Prefix = prefix,
                    NormalizedFullName = NormalizeDimName(fullName)
                });
            }
            catch
            {
                // ignore broken dimension
            }
        }

        return result;
    }

    private static bool MatchesFullName(
        string dimFullName,
        string dimNormalized,
        HashSet<string> rawTargets,
        HashSet<string> normalizedTargets)
    {
        if (rawTargets.Contains(dimFullName))
            return true;

        if (string.IsNullOrWhiteSpace(dimNormalized))
            return false;

        if (normalizedTargets.Contains(dimNormalized))
            return true;

        foreach (var target in normalizedTargets)
        {
            if (dimNormalized.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes SW dimension name strings to improve matching.
    /// - trims quotes/whitespace
    /// - removes "&lt;...&gt;" suffix
    /// - keeps only first 2 segments when split by '@' (Key@Sketch)
    /// </summary>
    private static string NormalizeDimName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        s = s.Trim().Trim('"');

        var idx = s.IndexOf('<');
        if (idx >= 0)
            s = s[..idx];

        var parts = s.Split('@');
        if (parts.Length >= 2)
            s = parts[0] + "@" + parts[1];

        return s;
    }

    private static string ExtractPrefix(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var atIdx = fullName.IndexOf('@');
        return atIdx >= 0 ? fullName[..atIdx] : fullName;
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
            {
                actualName = mapped;
            }

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
                    {
                        return v;
                    }
                }
                catch
                {
                    // ignore
                }

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
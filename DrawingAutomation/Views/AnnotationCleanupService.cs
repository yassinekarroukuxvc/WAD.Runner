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
/// - ReadDisplayDimensions accepts a computeNormalized flag: NormalizeDimName is only
///   invoked for the FullNameCleanup path, saving N string operations per view in the
///   ZeroCleanup and KeyCleanup paths.
/// - NormalizeDimName is span-based: no Trim('"') intermediate string, no Split('@')
///   array — only one .ToString() allocation at the end.
/// - RemoveZeroDimensionsFromDrawing builds zeroKeys directly into a HashSet and
///   extends it in-place, avoiding a full copy into a second collection.
/// - DeleteDimensionsByPredicate batch-selects all candidates and issues a single
///   DeleteSelection2 COM call. On failure it falls back to per-item deletion so
///   error granularity is never lost.
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

        /// <summary>
        /// Only populated when ReadDisplayDimensions is called with computeNormalized=true
        /// (i.e. the FullNameCleanup path). Empty string otherwise.
        /// </summary>
        public string NormalizedFullName { get; init; } = string.Empty;
    }

    // Reused static array — avoids a heap allocation on every call to
    // RemoveZeroDimensionsFromDrawing for the three always-included keys.
    private static readonly string[] AlwaysIncludedKeys = { "FX", "VR", "VRR" };

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

        // Diagnostics only — no normalized names needed.
        var infos = ReadDisplayDimensions(view, computeNormalized: false);
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
        IEnumerable<DimensionSpec> dims,
        bool hideVrExtremaAnnotations = false)
    {
        if (ds?.Model is not ModelDoc2 model) return;
        if (ds.Drawing is not DrawingDoc) return;
        if (drawingData?.Views == null) return;
        if (ctx == null) return;
        if (dims == null) return;

        // Build zeroKeys directly into a HashSet — no intermediate List, no Distinct() call.
        // VR_MAX / VR_MIN / VRR_MAX / VRR_MIN are excluded here; they are added below
        // only when their parent key is zero or hideVrExtremaAnnotations is set.
        var zeroKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in dims)
        {
            var keyStr = d.Key.ToString();
            if (string.IsNullOrWhiteSpace(keyStr)) continue;
            if (keyStr.Equals("VR_MAX", StringComparison.OrdinalIgnoreCase)) continue;
            if (keyStr.Equals("VR_MIN", StringComparison.OrdinalIgnoreCase)) continue;
            if (keyStr.Equals("VRR_MAX", StringComparison.OrdinalIgnoreCase)) continue;
            if (keyStr.Equals("VRR_MIN", StringComparison.OrdinalIgnoreCase)) continue;

            TryAddIfZero(zeroKeys, keyStr, ctx);
        }

        // Always evaluate the three implicit keys.
        foreach (var key in AlwaysIncludedKeys)
            TryAddIfZero(zeroKeys, key, ctx);

        // Extend zeroKeys in-place with derived extrema prefixes — no copy needed.
        if (zeroKeys.Contains("VR"))
        {
            zeroKeys.Add("VR_MAX");
            zeroKeys.Add("VR_MIN");
        }

        if (zeroKeys.Contains("VRR"))
        {
            zeroKeys.Add("VRR_MAX");
            zeroKeys.Add("VRR_MIN");
        }

        if (hideVrExtremaAnnotations)
        {
            zeroKeys.Add("VR_MAX");
            zeroKeys.Add("VR_MIN");
            zeroKeys.Add("VRR_MAX");
            zeroKeys.Add("VRR_MIN");

            Logger.Info("[ZeroCleanup] Overlay non_std_cut was compressed/clamped. " +
                        "Forcing deletion of VR/VRR extrema annotations: VR_MAX, VR_MIN, VRR_MAX, VRR_MIN.");
        }

        if (zeroKeys.Count == 0)
        {
            Logger.Info("[ZeroCleanup] No keys/prefixes marked for deletion.");
            return;
        }

        Logger.Info($"[ZeroCleanup] Annotation prefixes to delete: {string.Join(", ", zeroKeys)}");

        int totalDeleted = 0;

        foreach (var logicalViewName in drawingData.Views.Keys)
        {
            var view = FindView(ds, logicalViewName, nameMap);
            if (view == null)
            {
                Logger.Warn($"[ZeroCleanup] View '{logicalViewName}' not found.");
                continue;
            }

            // ZeroCleanup matches by Prefix only — NormalizedFullName is never read.
            var infos = ReadDisplayDimensions(view, computeNormalized: false);
            if (infos.Count == 0)
                continue;

            int deletedInView = DeleteDimensionsByPredicate(
                model,
                infos,
                info => zeroKeys.Contains(info.Prefix),
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

        // KeyCleanup matches by Prefix only — NormalizedFullName is never read.
        var infos = ReadDisplayDimensions(view, computeNormalized: false);
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

        // FullNameCleanup is the only path that needs NormalizedFullName per dimension.
        var infos = ReadDisplayDimensions(view, computeNormalized: true);
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

    /// <summary>
    /// Batch-selects all matching annotations and issues a SINGLE DeleteSelection2 COM call.
    /// This reduces COM round-trips from 2N (Select + Delete per item) to N+1 in the common
    /// case where all selections succeed.
    ///
    /// On batch-delete failure the method falls back to per-item deletion, preserving
    /// full error-granularity logging.
    /// </summary>
    private static int DeleteDimensionsByPredicate(
        ModelDoc2 model,
        IReadOnlyList<DisplayDimInfo> infos,
        Func<DisplayDimInfo, bool> shouldDelete,
        Func<DisplayDimInfo, string> successMessage,
        Func<DisplayDimInfo, Exception, string> failureMessage)
    {
        // ---- Phase 1: collect candidates (no COM calls) -------------------------
        var candidates = new List<DisplayDimInfo>();
        for (int i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            if (shouldDelete(info) && info.Annotation != null)
                candidates.Add(info);
        }

        if (candidates.Count == 0)
            return 0;

        // ---- Phase 2: batch-select all candidates (N COM calls) -----------------
        // First annotation clears the existing selection; the rest append.
        var selected = new List<DisplayDimInfo>(candidates.Count);

        for (int i = 0; i < candidates.Count; i++)
        {
            var info = candidates[i];
            try
            {
                bool append = selected.Count > 0;
                info.Annotation!.Select2(append, -1);
                selected.Add(info);
            }
            catch
            {
                // Silently skip any annotation whose Select2 throws;
                // it will simply be absent from the batch.
            }
        }

        if (selected.Count == 0)
            return 0;

        // ---- Phase 3: single DeleteSelection2 (1 COM call) ----------------------
        try
        {
            model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);

            foreach (var info in selected)
                Logger.Info(successMessage(info));

            return selected.Count;
        }
        catch
        {
            // Batch delete failed (e.g. mixed absorbed/non-absorbed items, SW state issue).
            // Fall back to per-item path to preserve error granularity.
        }

        // ---- Phase 4: per-item fallback -----------------------------------------
        int deleted = 0;

        foreach (var info in selected)
        {
            try
            {
                // Use append=false each time to reset selection before each individual delete.
                info.Annotation!.Select2(false, -1);
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

    /// <summary>
    /// Reads all DisplayDimensions from a view into a flat list.
    ///
    /// <paramref name="computeNormalized"/>: pass true only when NormalizedFullName will
    /// actually be used (i.e. the FullNameCleanup path). For ZeroCleanup and KeyCleanup,
    /// passing false skips NormalizeDimName entirely, saving one string operation per
    /// dimension across every view in the drawing.
    /// </summary>
    private static List<DisplayDimInfo> ReadDisplayDimensions(View view, bool computeNormalized)
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
                    // Skip NormalizeDimName when caller will never read NormalizedFullName.
                    NormalizedFullName = computeNormalized ? NormalizeDimName(fullName) : string.Empty
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
    /// Span-based: no intermediate string allocations — only one .ToString() at the end.
    ///   - Trims whitespace and surrounding quotes
    ///   - Removes "&lt;...&gt;" suffix
    ///   - Keeps only the first two '@'-delimited segments (Key@Sketch)
    /// </summary>
    private static string NormalizeDimName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var span = s.AsSpan().Trim();

        // Trim surrounding quotes if present.
        if (span.Length >= 2 && span[0] == '"' && span[span.Length - 1] == '"')
            span = span[1..^1];

        // Remove everything from the first '<' onward.
        var ltIdx = span.IndexOf('<');
        if (ltIdx >= 0)
            span = span[..ltIdx];

        // Keep only Key@Sketch (first two segments when split by '@').
        var atIdx = span.IndexOf('@');
        if (atIdx >= 0)
        {
            var afterAt = span[(atIdx + 1)..];
            var secondAt = afterAt.IndexOf('@');
            if (secondAt >= 0)
                span = span[..(atIdx + 1 + secondAt)];
        }

        return span.ToString();
    }

    private static string ExtractPrefix(string fullName)
    {
        var atIdx = fullName.IndexOf('@');
        return atIdx >= 0 ? fullName[..atIdx] : fullName;
    }

    /// <summary>
    /// Evaluates one key and adds it to <paramref name="zeroKeys"/> if its value is
    /// effectively zero (abs &lt; 1e-6). Extracted to avoid duplicating the try/catch
    /// inside the foreach loops of RemoveZeroDimensionsFromDrawing.
    /// </summary>
    private static void TryAddIfZero(HashSet<string> zeroKeys, string keyStr, LayoutContext ctx)
    {
        try
        {
            double value = DimensionKeyPolicy.IsAngle(keyStr)
                ? LayoutMath.Ddeg(ctx, keyStr)
                : LayoutMath.Dmm(ctx, keyStr);

            if (Math.Abs(value) < 1e-6)
                zeroKeys.Add(keyStr);
        }
        catch
        {
            // Key not present in context — skip.
        }
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
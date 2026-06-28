using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DataManagement.Infrastructure.Mapping;

namespace WAD.Runner.DrawingAutomation.Views;


public static class AnnotationCleanupService
{


    private sealed class DisplayDimInfo
    {
        public DisplayDimension DisplayDimension { get; init; } = null!;
        public Annotation? Annotation { get; init; }
        public string Name { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;


        public string NormalizedFullName { get; init; } = string.Empty;
    }


    private static readonly string[] AlwaysIncludedKeys = { "FX", "VR", "VRR" };


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


        foreach (var key in AlwaysIncludedKeys)
            TryAddIfZero(zeroKeys, key, ctx);


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

        bool deleteIsaBecauseVrExists = IsPositiveLength(ctx, "VR");

        if (deleteIsaBecauseVrExists)
        {
            Logger.Info("[ZeroCleanup] VR exists. Forcing deletion of ISA annotation in Detail view.");
        }

        if (zeroKeys.Count == 0 && !deleteIsaBecauseVrExists)
        {
            Logger.Info("[ZeroCleanup] No keys/prefixes marked for deletion.");
            return;
        }

        if (zeroKeys.Count > 0)
        {
            Logger.Info($"[ZeroCleanup] Annotation prefixes to delete: {string.Join(", ", zeroKeys)}");
        }

        int totalDeleted = 0;

        foreach (var logicalViewName in drawingData.Views.Keys)
        {
            var view = FindView(ds, logicalViewName, nameMap);
            if (view == null)
            {
                Logger.Warn($"[ZeroCleanup] View '{logicalViewName}' not found.");
                continue;
            }


            var infos = ReadDisplayDimensions(view, computeNormalized: false);
            if (infos.Count == 0)
                continue;

            bool isDetailView = string.Equals(logicalViewName, "Detail", StringComparison.OrdinalIgnoreCase);

            int deletedInView = DeleteDimensionsByPredicate(
                model,
                infos,
                info =>
                    zeroKeys.Contains(info.Prefix) ||
                    (
                        deleteIsaBecauseVrExists &&
                        isDetailView &&
                        info.Prefix.Equals("ISA", StringComparison.OrdinalIgnoreCase)
                    ),
                info =>
                    info.Prefix.Equals("ISA", StringComparison.OrdinalIgnoreCase)
                        ? $"[ZeroCleanup] Deleted dim '{info.FullName}' in view '{logicalViewName}' because VR exists."
                        : $"[ZeroCleanup] Deleted dim '{info.FullName}' in view '{logicalViewName}' for key '{info.Prefix}'.",
                (info, ex) =>
                    info.Prefix.Equals("ISA", StringComparison.OrdinalIgnoreCase)
                        ? $"[ZeroCleanup] Failed to delete ISA dim '{info.FullName}' in view '{logicalViewName}' while VR exists: {ex.Message}"
                        : $"[ZeroCleanup] Failed to delete dim '{info.FullName}' in view '{logicalViewName}' (key='{info.Prefix}'): {ex.Message}");

            totalDeleted += deletedInView;
        }

        Logger.Info($"[ZeroCleanup] Total deleted dimension annotations: {totalDeleted}");
    }


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


    private static int DeleteDimensionsByPredicate(
        ModelDoc2 model,
        IReadOnlyList<DisplayDimInfo> infos,
        Func<DisplayDimInfo, bool> shouldDelete,
        Func<DisplayDimInfo, string> successMessage,
        Func<DisplayDimInfo, Exception, string> failureMessage)
    {

        var candidates = new List<DisplayDimInfo>();
        for (int i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            if (shouldDelete(info) && info.Annotation != null)
                candidates.Add(info);
        }

        if (candidates.Count == 0)
            return 0;


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


            }
        }

        if (selected.Count == 0)
            return 0;


        try
        {
            model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);

            foreach (var info in selected)
                Logger.Info(successMessage(info));

            return selected.Count;
        }
        catch
        {


        }


        int deleted = 0;

        foreach (var info in selected)
        {
            try
            {

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

                    NormalizedFullName = computeNormalized ? NormalizeDimName(fullName) : string.Empty
                });
            }
            catch
            {

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


    private static string NormalizeDimName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var span = s.AsSpan().Trim();


        if (span.Length >= 2 && span[0] == '"' && span[span.Length - 1] == '"')
            span = span[1..^1];


        var ltIdx = span.IndexOf('<');
        if (ltIdx >= 0)
            span = span[..ltIdx];


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

        }
    }

    private static bool IsPositiveLength(LayoutContext ctx, string keyStr)
    {
        try
        {
            return Math.Abs(LayoutMath.Dmm(ctx, keyStr)) >= 1e-6;
        }
        catch
        {
            return false;
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

            v = v.IGetNextView();

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

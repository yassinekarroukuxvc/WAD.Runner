using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;                          // Logger
using WAD.Runner.DataManagement.Domain.Drawing;       // DrawingData
using WAD.Runner.DataManagement.Domain.Planning;      // LayoutContext, LayoutMath, DimensionSpec
using WAD.Runner.DrawingAutomation.SolidWorks;        // DrawingService
using WAD.Runner.DataManagement.Infrastructure.Mapping; // DimensionKeyPolicy

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Centralized helpers for cleaning up annotations on a drawing,
/// e.g. removing dimensions whose value is 0 based on planning data.
/// </summary>
public static class AnnotationCleanupService
{
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

        // Build candidate key set:
        //  - All keys from planned dims
        //  - Plus some known extra keys (FX/VR/VRR) that often have driven dims
        var candidateKeyStrings = dims
            .Select(d => d.Key.ToString())
            .Concat(new[] { "FX", "VR", "VRR" }) // explicit extra keys
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Determine which of these keys are actually zero (mm or deg)
        var zeroKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyStr in candidateKeyStrings)
        {
            double value;
            try
            {
                // Decide which unit to read based on DimensionKeyPolicy
                if (DimensionKeyPolicy.IsAngle(keyStr))
                {
                    // Angle key → check in degrees
                    value = LayoutMath.Ddeg(ctx, keyStr);
                }
                else
                {
                    // Length key → check in mm
                    value = LayoutMath.Dmm(ctx, keyStr);
                }
            }
            catch
            {
                // If LayoutMath can't resolve it, skip
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

        // For each view in the drawing, attempt to delete annotations for these zero keys
        foreach (var logicalViewName in drawingData.Views.Keys)
        {
            foreach (var key in zeroKeys)
            {
                totalDeleted += DeleteDimensionAnnotationsForKey(ds, nameMap, logicalViewName, key);
            }
        }

        Logger.Info($"[ZeroCleanup] Total deleted dimension annotations: {totalDeleted}");
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

            Dimension dim = null!;
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
                // ignore, keep fullName empty
            }

            string prefix = fullName;
            var atIdx = prefix.IndexOf('@');
            if (atIdx >= 0)
                prefix = prefix[..atIdx];

            bool matchByKey = string.Equals(prefix, key, StringComparison.OrdinalIgnoreCase);
            if (!matchByKey)
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
            if (nameMap != null && nameMap.TryGetValue(logicalName, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
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
            Logger.Warn($"[ZeroCleanup.FindView('{logicalName}')] failed: {ex.Message}");
        }
        return null;
    }
}

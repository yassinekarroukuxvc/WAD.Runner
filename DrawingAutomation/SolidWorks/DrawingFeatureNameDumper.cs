// DrawingAutomation/SolidWorks/DrawingFeatureNameDumper.cs
using System;
using System.Collections.Generic;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.SolidWorks;

/// <summary>
/// Diagnostic utility: dumps all feature names from a drawing's FeatureManager.
///
/// Use this during development to discover the exact SolidWorks names of sketches,
/// detail circles, section lines, and other drawing features — so you can then
/// register them in the appropriate IDrawingFeatureRuleSet implementation.
///
/// Usage in your pipeline (temporary, remove after discovering names):
///   DrawingFeatureNameDumper.DumpAll(ds, tag: "COB_Production");
///
/// Check the WAD.Runner log output for lines like:
///   [FeatureDump/COB_Production] Feature[0]: 'DetailCircle1' (type=DetailCbore)
///   [FeatureDump/COB_Production]   Sub[0]: 'Sketch2' (type=ProfileFeature)
/// </summary>
public static class DrawingFeatureNameDumper
{
    /// <summary>
    /// Dumps every feature and sub-feature name from the drawing's FeatureManager.
    /// </summary>
    public static void DumpAll(DrawingService ds, string tag = "Drawing")
    {
        try
        {
            var model = ds?.Model;
            if (model is null)
            {
                Logger.Warn($"[FeatureDump/{tag}] model is null — cannot dump.");
                return;
            }

            Logger.Info($"[FeatureDump/{tag}] ── Drawing FeatureManager dump ──────────────────");

            int featureIdx = 0;
            var f = model.FirstFeature() as Feature;

            while (f != null)
            {
                DumpFeature(f, featureIdx, depth: 0, tag: tag);

                int subIdx = 0;
                var sub = f.GetFirstSubFeature() as Feature;
                while (sub != null)
                {
                    DumpFeature(sub, subIdx, depth: 1, tag: tag);
                    sub = sub.GetNextSubFeature() as Feature;
                    subIdx++;
                }

                f = f.GetNextFeature() as Feature;
                featureIdx++;
            }

            Logger.Info($"[FeatureDump/{tag}] ── End of dump ─────────────────────────────────");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[FeatureDump/{tag}] Dump failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Dumps only features whose name contains the given substring (case-insensitive).
    /// Useful for narrowing down a large feature tree.
    /// </summary>
    public static void DumpMatching(DrawingService ds, string nameContains, string tag = "Drawing")
    {
        try
        {
            var model = ds?.Model;
            if (model is null) return;

            Logger.Info($"[FeatureDump/{tag}] ── Features matching '{nameContains}' ──────────");

            var f = model.FirstFeature() as Feature;
            while (f != null)
            {
                TryDumpIfMatches(f, nameContains, depth: 0, tag: tag);

                var sub = f.GetFirstSubFeature() as Feature;
                while (sub != null)
                {
                    TryDumpIfMatches(sub, nameContains, depth: 1, tag: tag);
                    sub = sub.GetNextSubFeature() as Feature;
                }

                f = f.GetNextFeature() as Feature;
            }

            Logger.Info($"[FeatureDump/{tag}] ── End ──────────────────────────────────────────");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[FeatureDump/{tag}] DumpMatching failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns all feature names as a list (useful for programmatic inspection).
    /// </summary>
    public static IReadOnlyList<string> CollectAllNames(DrawingService ds)
    {
        var names = new List<string>();
        try
        {
            var model = ds?.Model;
            if (model is null) return names;

            var f = model.FirstFeature() as Feature;
            while (f != null)
            {
                TryCollect(f, names);
                var sub = f.GetFirstSubFeature() as Feature;
                while (sub != null)
                {
                    TryCollect(sub, names);
                    sub = sub.GetNextSubFeature() as Feature;
                }
                f = f.GetNextFeature() as Feature;
            }
        }
        catch { /* best effort */ }
        return names;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void DumpFeature(Feature f, int idx, int depth, string tag)
    {
        try
        {
            var name = f.Name ?? "(null)";
            var typeName = f.GetTypeName2() ?? "(unknown)";
            bool suppressed = TryGetSuppressed(f);
            var indent = depth == 0 ? string.Empty : "  ";
            var label = depth == 0 ? $"Feature[{idx}]" : $"Sub[{idx}]";

            Logger.Info(
                $"[FeatureDump/{tag}] {indent}{label}: '{name}' " +
                $"(type={typeName}, suppressed={suppressed})");
        }
        catch { /* corrupted COM object */ }
    }

    private static void TryDumpIfMatches(Feature f, string contains, int depth, string tag)
    {
        try
        {
            var name = f.Name ?? string.Empty;
            if (name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                DumpFeature(f, -1, depth, tag);
        }
        catch { }
    }

    private static void TryCollect(Feature f, List<string> names)
    {
        try
        {
            var name = f.Name;
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }
        catch { }
    }

    private static bool TryGetSuppressed(Feature f)
    {
        try
        {
            var raw = f.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null);
            if (raw is bool b) return b;
            if (raw is int i) return i != 0;
            if (raw is Array arr && arr.Length > 0 && arr.GetValue(0) is bool b2) return b2;
            return false;
        }
        catch { return false; }
    }
}

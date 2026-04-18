using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Overlay;

public static class DrawingViewConfigBinder
{
    /// <summary>
    /// Backward-compatible overload.
    /// Uses the old behavior with no wedge-specific overlay cut logic.
    /// </summary>
    public static bool SetReferencedConfigurationForView(
        ModelDoc2 model,
        string viewName,
        WedgeSubclass subclass,
        DrawingType drawingType)
    {
        return SetReferencedConfigurationForView(
            model,
            viewName,
            subclass,
            drawingType,
            wedgeType: null,
            hasVw: false,
            hasVr: false);
    }

    /// <summary>
    /// Backward-compatible overload.
    /// Keeps old signature, but resolves using the same string for both logical and actual names.
    /// </summary>
    public static bool SetReferencedConfigurationForView(
        ModelDoc2 model,
        string viewName,
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeType? wedgeType,
        bool hasVw,
        bool hasVr)
    {
        return SetReferencedConfigurationForLogicalView(
            model,
            logicalViewName: viewName,
            actualViewName: viewName,
            subclass,
            drawingType,
            wedgeType,
            hasVw,
            hasVr);
    }

    /// <summary>
    /// Preferred overload: resolve config using a stable logical view name
    /// (Front/Side/Top/Detail/Section), then apply it to the mapped actual SW view.
    /// </summary>
    public static bool SetReferencedConfigurationForLogicalView(
        ModelDoc2 model,
        string logicalViewName,
        string actualViewName,
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeType? wedgeType,
        bool hasVw,
        bool hasVr)
    {
        if (model is null || string.IsNullOrWhiteSpace(actualViewName))
            return false;

        if (model is not DrawingDoc dd)
        {
            Logger.Warn($"[ConfigBind] Not a DrawingDoc: cannot bind config for '{actualViewName}'.");
            return false;
        }

        var v = FindViewByName(dd, actualViewName);
        if (v is null)
        {
            Logger.Warn($"[ConfigBind] View '{actualViewName}' not found in drawing.");
            return false;
        }

        var target = GetConfigNameForLogicalView(
            logicalViewName,
            subclass,
            drawingType,
            wedgeType,
            hasVw,
            hasVr);

        if (string.IsNullOrWhiteSpace(target))
        {
            Logger.Warn(
                $"[ConfigBind] No config resolved for LogicalView='{logicalViewName}', ActualView='{actualViewName}', Subclass='{subclass}', DrawingType='{drawingType}', WedgeType='{wedgeType}'.");
            return false;
        }

        Logger.Info(
            $"[ConfigBind] LogicalView='{logicalViewName}', ActualView='{actualViewName}' → target configuration '{target}'.");

        // IMPORTANT:
        // Apply only to the target view itself.
        // Do NOT force the same config into the base view, otherwise configs can bleed
        // between Detail / Section / Front chains.
        TrySetConfig(v, target, $"view('{SafeName(v)}')");

        TryRebuild(model);

        var actual = SafeGetRefConfig(v);

        Logger.Info(
            $"[ConfigBind] '{SafeName(v)}' → '{actual}' (requested '{target}')");

        return actual.Equals(target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Backward-compatible overload.
    /// </summary>
    public static bool SetReferencedConfigurationForViews(
        ModelDoc2 model,
        WedgeSubclass subclass,
        DrawingType drawingType,
        params string[] viewNames)
    {
        return SetReferencedConfigurationForViews(
            model,
            subclass,
            drawingType,
            wedgeType: null,
            hasVw: false,
            hasVr: false,
            viewNames);
    }

    /// <summary>
    /// Backward-compatible overload.
    /// Uses the same string as both logical and actual view name.
    /// </summary>
    public static bool SetReferencedConfigurationForViews(
        ModelDoc2 model,
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeType? wedgeType,
        bool hasVw,
        bool hasVr,
        params string[] viewNames)
    {
        var any = false;

        foreach (var n in viewNames.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            any |= SetReferencedConfigurationForLogicalView(
                model,
                logicalViewName: n!,
                actualViewName: n!,
                subclass,
                drawingType,
                wedgeType,
                hasVw,
                hasVr);
        }

        return any;
    }

    // ---------- internals ----------

    private static View? FindViewByName(DrawingDoc dd, string name)
    {
        string target = Normalize(name);
        foreach (var v in EnumerateUserViews(dd))
        {
            if (Normalize(SafeName(v)) == target)
                return v;
        }
        return null;
    }

    /// <summary>
    /// Enumerate only actual user/model views (skip sheet placeholders).
    /// </summary>
    private static IEnumerable<View> EnumerateUserViews(DrawingDoc dd)
    {
        for (var v = dd.GetFirstView() as View; v != null; v = v.GetNextView() as View)
        {
            if (v.ReferencedDocument is ModelDoc2)
                yield return v;
        }
    }

    private static void TrySetConfig(View v, string cfg, string label)
    {
        try
        {
            v.ReferencedConfiguration = cfg;
            Logger.Info($"[ConfigBind] Set {label} to '{cfg}'.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ConfigBind] Failed set {label}: {ex.Message}");
        }
    }

    private static void TryRebuild(ModelDoc2 model)
    {
        try { model.ForceRebuild3(false); } catch { }
        try { model.EditRebuild3(); } catch { }
    }

    private static string SafeGetRefConfig(View v)
    {
        try { return v.ReferencedConfiguration ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string SafeName(View v)
    {
        try { return v.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string Normalize(string? s)
        => Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();

    private static bool IsOverlayCutWedgeType(WedgeType? wedgeType)
    {
        if (wedgeType is null)
            return false;

        var name = wedgeType.Value.ToString()
            .Replace("/", "")
            .Replace("_", "")
            .Replace("-", "")
            .ToUpperInvariant();

        return name is "COB" or "FP" or "UTUS";
    }

    /// <summary>
    /// Resolve by logical view identity, not by parsing the actual SW view name.
    /// This is the key fix.
    /// </summary>
    private static string GetConfigNameForLogicalView(
        string logicalViewName,
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeType? wedgeType,
        bool hasVw,
        bool hasVr)
    {
        var logical = Normalize(logicalViewName);

        // Non-overlay behavior stays unchanged.
        if (drawingType != DrawingType.Overlay)
        {
            return (subclass, drawingType) switch
            {
                (WedgeSubclass.PGB, DrawingType.Customer) => "PGB_CUSTOMER_DRAWING",
                (WedgeSubclass.PGB, DrawingType.Production) => "PGB_DRAWING",

                (WedgeSubclass.FG, DrawingType.Customer) => "FG_CUSTOMER_DRAWING",
                (WedgeSubclass.FG, DrawingType.Production) => "FG_PRODUCTION_DRAWING",

                _ => string.Empty
            };
        }

        // Overlay behavior
        if (subclass == WedgeSubclass.PGB)
        {
            // PGB overlay rules remain unchanged.
            return "PGB_OVERLAY";
        }

        if (subclass == WedgeSubclass.FG)
        {
            // FG overlay: apply extra cut configs only for COB / FP / UTUS.
            if (IsOverlayCutWedgeType(wedgeType))
            {
                bool isDetail = logical == "detail";
                bool isSection = logical == "section";

                // If there is no VW and no VR, the section and detail views use std_cut.
                if (!hasVw && !hasVr && (isDetail || isSection))
                    return "std_cut";

                // If both VW and VR are present, only the detail view uses non_std_cut.
                if (hasVw && hasVr && isDetail)
                    return "non_std_cut";

                if (hasVw && hasVr && isSection)
                    return "std_cut";
            }

            // Otherwise FG overlay remains unchanged.
            return "FG_OVERLAY";
        }

        return string.Empty;
    }
}
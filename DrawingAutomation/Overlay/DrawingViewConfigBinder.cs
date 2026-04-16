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
    /// New overload with wedge-specific overlay rules.
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
        if (model is null || string.IsNullOrWhiteSpace(viewName))
            return false;

        if (model is not DrawingDoc dd)
        {
            Logger.Warn($"[ConfigBind] Not a DrawingDoc: cannot bind config for '{viewName}'.");
            return false;
        }

        var v = FindViewByName(dd, viewName);
        if (v is null)
        {
            Logger.Warn($"[ConfigBind] View '{viewName}' not found in drawing.");
            return false;
        }

        var target = GetConfigName(viewName, subclass, drawingType, wedgeType, hasVw, hasVr);
        if (string.IsNullOrWhiteSpace(target))
        {
            Logger.Warn(
                $"[ConfigBind] No config resolved for View='{viewName}', Subclass='{subclass}', DrawingType='{drawingType}', WedgeType='{wedgeType}'.");
            return false;
        }

        Logger.Info($"[ConfigBind] Target configuration for view '{viewName}' → '{target}'.");

        var baseView = v.GetBaseView() as View;
        if (baseView != null)
            TrySetConfig(baseView, target, $"base('{SafeName(baseView)}')");

        TrySetConfig(v, target, $"view('{SafeName(v)}')");

        TryRebuild(model);

        var actual = SafeGetRefConfig(v);
        var baseActual = baseView != null ? SafeGetRefConfig(baseView) : string.Empty;

        Logger.Info(
            $"[ConfigBind] '{SafeName(v)}' → '{actual}' (requested '{target}')" +
            (baseView != null
                ? $"; base '{SafeName(baseView)}' → '{baseActual}'"
                : string.Empty));

        return actual.Equals(target, StringComparison.OrdinalIgnoreCase)
            || (baseView != null && baseActual.Equals(target, StringComparison.OrdinalIgnoreCase));
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
    /// New overload with wedge-specific overlay rules.
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
            any |= SetReferencedConfigurationForView(
                model,
                n!,
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

        var name = wedgeType.Value.ToString().Replace("/", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
        return name is "COB" or "FP" or "UTUS";
    }

    private static bool IsDetailView(string viewName)
    {
        var normalized = Normalize(viewName);
        return normalized.Contains("detail");
    }

    private static bool IsSectionView(string viewName)
    {
        var normalized = Normalize(viewName);
        return normalized.Contains("section");
    }

    /// <summary>
    /// View-aware configuration resolver.
    ///
    /// Rules:
    /// - PGB overlay remains unchanged.
    /// - FG overlay remains unchanged except for COB/FP/UTUS:
    ///   * if no VW and no VR => detail + section use std_cut
    ///   * if VW and VR both present => detail uses non_std_cut
    /// </summary>
    private static string GetConfigName(
        string viewName,
        WedgeSubclass subclass,
        DrawingType drawingType,
        WedgeType? wedgeType,
        bool hasVw,
        bool hasVr)
    {
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
                var isDetail = IsDetailView(viewName);
                var isSection = IsSectionView(viewName);

                // If there is no VW and no VR, section + detail use std_cut.
                if (!hasVw && !hasVr && (isDetail || isSection))
                    return "std_cut";

                // If both VW and VR are present, detail uses non_std_cut.
                if (hasVw && hasVr && isDetail)
                    return "non_std_cut";
            }

            // Otherwise FG overlay remains unchanged.
            return "FG_OVERLAY";
        }

        return string.Empty;
    }
}
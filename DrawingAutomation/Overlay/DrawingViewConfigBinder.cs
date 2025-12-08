using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Overlay;

public static class DrawingViewConfigBinder
{
    public static bool SetReferencedConfigurationForView(
        ModelDoc2 model,
        string viewName,
        WedgeSubclass subclass,
        DrawingType drawingType)
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

        // Resolve target config name using the same mapping as PartAutomationService.ActivateConfiguration
        var target = GetConfigName(subclass, drawingType);
        if (string.IsNullOrWhiteSpace(target))
        {
            Logger.Warn($"[ConfigBind] No config resolved for {subclass}+{drawingType}.");
            return false;
        }

        Logger.Info($"[ConfigBind] Target configuration → '{target}'.");

        // Set immediate base first (inheritance), then this view
        var baseView = v.GetBaseView() as View;
        if (baseView != null)
            TrySetConfig(baseView, target, $"base('{SafeName(baseView)}')");

        TrySetConfig(v, target, $"view('{SafeName(v)}')");

        // Rebuild via ModelDoc2
        TryRebuild(model);

        // Verify
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

    public static bool SetReferencedConfigurationForViews(
        ModelDoc2 model,
        WedgeSubclass subclass,
        DrawingType drawingType,
        params string[] viewNames)
    {
        var any = false;
        foreach (var n in viewNames.Where(s => !string.IsNullOrWhiteSpace(s)))
            any |= SetReferencedConfigurationForView(model, n!, subclass, drawingType);
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
            // Only yield real drawing views (with a referenced model)
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
        try { model.ForceRebuild3(false); } catch { /* ignore */ }
        try { model.EditRebuild3(); } catch { /* ignore */ }
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

    /// <summary>
    /// Configuration resolver aligned with PartAutomationService.ActivateConfiguration.
    /// </summary>
    private static string GetConfigName(WedgeSubclass subclass, DrawingType type)
        => (subclass, type) switch
        {
            (WedgeSubclass.PGB, DrawingType.Overlay) => "PGB_OVERLAY",
            (WedgeSubclass.PGB, DrawingType.Customer) => "PGB_CUSTOMER_DRAWING",
            (WedgeSubclass.PGB, DrawingType.Production) => "PGB_DRAWING",

            (WedgeSubclass.FG, DrawingType.Overlay) => "FG_OVERLAY",
            (WedgeSubclass.FG, DrawingType.Customer) => "FG_CUSTOMER_DRAWING",
            (WedgeSubclass.FG, DrawingType.Production) => "FG_PRODUCTION_DRAWING",

            _ => string.Empty
        };
}
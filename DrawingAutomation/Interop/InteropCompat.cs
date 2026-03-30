// DrawingAutomation/Interop/InteropCompat.cs
using System;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.DrawingAutomation.Interop;

/// <summary>
/// Centralized fallbacks for tricky interop calls across SW versions.
/// Keep all reflection/dynamic tries here.
/// </summary>
internal static class InteropCompat
{
    // ── View alignment / locking ──────────────────────────────────────────

    public static void TryBreakAlignment(View v)
    {
        if (v is null) return;

        try { dynamic dv = v; dv.BreakAlignment(); return; } catch { /* try next */ }
        TryInvokeNoArgs(v, "BreakAlignment");
        TryInvokeNoArgs(v, "BreakParentAlignment");
    }

    public static void TryUnlock(View v)
    {
        if (v is null) return;
        try { v.PositionLocked = false; } catch { /* ignore */ }
    }

    // ── View scale ────────────────────────────────────────────────────────

    public static double GetScaleDecimalOr(View v, double fallback = 1.0)
    {
        if (v is null) return fallback;
        try { return v.ScaleDecimal; } catch { return fallback; }
    }

    public static void TrySetScale(View v, double value)
    {
        if (v is null) return;
        try { v.ScaleDecimal = value; }
        catch
        {
            try { v.GetType().GetMethod("SetScale")?.Invoke(v, new object[] { value }); } catch { }
        }
    }

    // ── View outline (for autoscale) ──────────────────────────────────────

    /// <summary>
    /// Try to read view outline as meters (x1,y1,x2,y2). Returns true on success.
    /// Supports GetOutline() returning double[] or object[], and the ref-args variation.
    /// </summary>
    public static bool TryGetViewOutline(View v, out double x1, out double y1, out double x2, out double y2)
    {
        x1 = y1 = x2 = y2 = 0.0;
        if (v is null) return false;

        // pattern 1: GetOutline() -> double[4]
        try
        {
            var arrObj = v.GetOutline();
            if (arrObj is double[] d && d.Length >= 4)
            {
                x1 = d[0]; y1 = d[1]; x2 = d[2]; y2 = d[3];
                return true;
            }
            if (arrObj is object[] o && o.Length >= 4)
            {
                x1 = Convert.ToDouble(o[0], CultureInfo.InvariantCulture);
                y1 = Convert.ToDouble(o[1], CultureInfo.InvariantCulture);
                x2 = Convert.ToDouble(o[2], CultureInfo.InvariantCulture);
                y2 = Convert.ToDouble(o[3], CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch { /* fall through */ }

        // pattern 2: GetOutline(ref x1, ref y1, ref x2, ref y2) via dynamic
        try
        {
            double rx1 = 0, ry1 = 0, rx2 = 0, ry2 = 0;
            dynamic dv = v;
            dv.GetOutline(ref rx1, ref ry1, ref rx2, ref ry2);
            x1 = rx1; y1 = ry1; x2 = rx2; y2 = ry2;
            return true;
        }
        catch { /* give up */ }

        return false;
    }

    // ── View → referenced model path (optional helper) ────────────────────

    public static string? TryGetReferencedModelPath(View v)
    {
        if (v is null) return null;

        // v.ReferencedDocument?.GetPathName()
        try
        {
            var p = (v.ReferencedDocument as ModelDoc2)?.GetPathName();
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        catch { }

        // dv.GetReferencedModelName2()
        try
        {
            dynamic dv = v;
            var p2 = dv.GetReferencedModelName2();
            if (p2 is string s2 && !string.IsNullOrWhiteSpace(s2)) return s2;
        }
        catch { }

        // dv.GetReferencedModelName()
        try
        {
            dynamic dv = v;
            var p3 = dv.GetReferencedModelName();
            if (p3 is string s3 && !string.IsNullOrWhiteSpace(s3)) return s3;
        }
        catch { }

        return null;
    }

    // ── internals ─────────────────────────────────────────────────────────

    private static bool TryInvokeNoArgs(object target, string methodName)
    {
        try
        {
            var mi = target.GetType().GetMethod(methodName);
            if (mi is null) return false;
            mi.Invoke(target, null);
            return true;
        }
        catch { return false; }
    }
}
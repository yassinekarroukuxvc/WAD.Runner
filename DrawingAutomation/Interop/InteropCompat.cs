// DrawingAutomation/Interop/InteropCompat.cs
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

using SolidWorks.Interop.sldworks;

namespace WAD.Runner.DrawingAutomation.Interop;

/// <summary>
/// Centralized fallbacks for tricky interop calls across SW versions.
/// Keep all reflection tries here.
///
//// PERFORMANCE:
/// - Avoid dynamic binder overhead.
/// - Cache reflected methods per runtime type.
/// </summary>
internal static class InteropCompat
{
    private static readonly ConcurrentDictionary<(Type Type, string Name, int Arity), MethodInfo?> MethodCache = new();

    // ── View alignment / locking ──────────────────────────────────────────

    public static void TryBreakAlignment(View v)
    {
        if (v is null) return;

        if (TryInvokeNoArgs(v, "BreakAlignment")) return;
        TryInvokeNoArgs(v, "BreakParentAlignment");
    }

    public static void TryUnlock(View v)
    {
        if (v is null) return;
        try { v.PositionLocked = false; } catch { }
    }

    // ── View scale ────────────────────────────────────────────────────────

    public static double GetScaleDecimalOr(View v, double fallback = 1.0)
    {
        if (v is null) return fallback;

        try { return v.ScaleDecimal; }
        catch { return fallback; }
    }

    public static void TrySetScale(View v, double value)
    {
        if (v is null) return;

        try
        {
            v.ScaleDecimal = value;
            return;
        }
        catch
        {
            // fall through
        }

        try
        {
            var mi = GetMethod(v.GetType(), "SetScale", 1);
            if (mi != null)
                mi.Invoke(v, new object[] { value });
        }
        catch { }
    }

    // ── View outline (for autoscale) ──────────────────────────────────────

    /// <summary>
    /// Try to read view outline as meters (x1,y1,x2,y2). Returns true on success.
    /// Supports:
    /// - GetOutline() returning double[] or object[]
    /// - GetOutline(ref x1, ref y1, ref x2, ref y2) via reflection
    /// </summary>
    public static bool TryGetViewOutline(View v, out double x1, out double y1, out double x2, out double y2)
    {
        x1 = y1 = x2 = y2 = 0.0;
        if (v is null) return false;

        // pattern 1: GetOutline() -> array
        try
        {
            var arrObj = v.GetOutline();

            if (arrObj is double[] d && d.Length >= 4)
            {
                x1 = d[0];
                y1 = d[1];
                x2 = d[2];
                y2 = d[3];
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
        catch
        {
            // fall through
        }

        // pattern 2: GetOutline(ref x1, ref y1, ref x2, ref y2)
        try
        {
            var mi = GetMethod(v.GetType(), "GetOutline", 4);
            if (mi != null)
            {
                object[] args = { 0.0, 0.0, 0.0, 0.0 };
                mi.Invoke(v, args);

                x1 = Convert.ToDouble(args[0], CultureInfo.InvariantCulture);
                y1 = Convert.ToDouble(args[1], CultureInfo.InvariantCulture);
                x2 = Convert.ToDouble(args[2], CultureInfo.InvariantCulture);
                y2 = Convert.ToDouble(args[3], CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
            // give up
        }

        return false;
    }

    // ── View → referenced model path (optional helper) ────────────────────

    public static string? TryGetReferencedModelPath(View v)
    {
        if (v is null) return null;

        try
        {
            var p = (v.ReferencedDocument as ModelDoc2)?.GetPathName();
            if (!string.IsNullOrWhiteSpace(p))
                return p;
        }
        catch { }

        try
        {
            var mi2 = GetMethod(v.GetType(), "GetReferencedModelName2", 0);
            if (mi2 != null)
            {
                var p2 = mi2.Invoke(v, null) as string;
                if (!string.IsNullOrWhiteSpace(p2))
                    return p2;
            }
        }
        catch { }

        try
        {
            var mi = GetMethod(v.GetType(), "GetReferencedModelName", 0);
            if (mi != null)
            {
                var p3 = mi.Invoke(v, null) as string;
                if (!string.IsNullOrWhiteSpace(p3))
                    return p3;
            }
        }
        catch { }

        return null;
    }

    // ── internals ─────────────────────────────────────────────────────────

    private static bool TryInvokeNoArgs(object target, string methodName)
    {
        try
        {
            var mi = GetMethod(target.GetType(), methodName, 0);
            if (mi is null) return false;

            mi.Invoke(target, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo? GetMethod(Type type, string methodName, int arity)
    {
        return MethodCache.GetOrAdd((type, methodName, arity), key =>
        {
            try
            {
                var methods = key.Type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!string.Equals(m.Name, key.Name, StringComparison.Ordinal))
                        continue;

                    var ps = m.GetParameters();
                    if (ps.Length == key.Arity)
                        return m;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        });
    }
}
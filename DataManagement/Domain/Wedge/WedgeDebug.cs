using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.Application;
namespace WAD.Runner.DataManagement.Domain.Wedge;

public static class WedgeDebug
{
    public static void DumpWedgeData(WedgeData wedge, string tag = "WedgeData")
    {
        if (wedge is null)
        {
            Logger.Warn($"[{tag}] wedge is null");
            return;
        }

        var sb = new StringBuilder(16_384);

        sb.AppendLine($"[{tag}] ─────────────────────────────────────────────");
        sb.AppendLine($"[{tag}] ArticleNumber: {wedge.ArticleNumber}");
        sb.AppendLine($"[{tag}] Subclass     : {wedge.Subclass}");

        // KValue / Marking (ToString() is usually good enough)
        sb.AppendLine($"[{tag}] KValue       : {(wedge.KValue is null ? "<null>" : wedge.KValue.ToString())}");
        sb.AppendLine($"[{tag}] Marking      : {(wedge.Marking is null ? "<null>" : wedge.Marking.ToString())}");

        // Properties
        sb.AppendLine($"[{tag}] Properties   : count={wedge.Properties?.Count ?? 0}");
        if (wedge.Properties is not null && wedge.Properties.Count > 0)
        {
            foreach (var kv in wedge.Properties.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"[{tag}]   PROP {kv.Key} = {(kv.Value is null ? "<null>" : kv.Value)}");
        }

        // Dimensions
        sb.AppendLine($"[{tag}] Dimensions   : count={wedge.Dimensions?.Count ?? 0}");
        if (wedge.Dimensions is not null && wedge.Dimensions.Count > 0)
        {
            foreach (var kv in wedge.Dimensions.OrderBy(k => k.Key.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                var key = kv.Key;
                var dim = kv.Value;

                sb.AppendLine($"[{tag}]   DIM {key}: {FormatDimension(dim)}");
            }
        }

        sb.AppendLine($"[{tag}] ─────────────────────────────────────────────");

        Logger.Info(sb.ToString());
    }

    // Best-effort formatting without assuming your Dimension internals too hard.
    private static string FormatDimension(Dimension? dim)
    {
        if (dim is null) return "<null>";

        try
        {
            // Common layout in your domain: dim.Nominal + tolerances (L/U) possibly in mm.
            // We'll probe fields/properties by name and print what exists.
            var t = dim.GetType();
            var nominal = GetMemberValue(dim, t, "Nominal");
            var ltol = GetMemberValue(dim, t, "Ltol") ?? GetMemberValue(dim, t, "LowerTolerance") ?? GetMemberValue(dim, t, "LTol");
            var utol = GetMemberValue(dim, t, "Utol") ?? GetMemberValue(dim, t, "UpperTolerance") ?? GetMemberValue(dim, t, "UTol");
            var comment = GetMemberValue(dim, t, "Comment") ?? GetMemberValue(dim, t, "Note");

            // If Nominal is a value object, try to extract common stuff like Value/Unit/IsMm/IsDeg
            var nominalText = FormatValueObject(nominal);

            var parts = new System.Collections.Generic.List<string>(8)
            {
                $"Nom={nominalText}"
            };

            if (ltol is not null) parts.Add($"Ltol={FormatValueObject(ltol)}");
            if (utol is not null) parts.Add($"Utol={FormatValueObject(utol)}");
            if (comment is not null) parts.Add($"Comment={comment}");

            // As a fallback, include ToString() for the full dim (often includes unit)
            var ts = dim.ToString();
            if (!string.IsNullOrWhiteSpace(ts) && ts != t.FullName)
                parts.Add($"ToString={ts}");

            return string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            return $"<format failed: {ex.GetType().Name}: {ex.Message}> ToString={dim}";
        }
    }

    private static object? GetMemberValue(object obj, Type t, string name)
    {
        try
        {
            // property
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(obj);

            // field
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(obj);

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatValueObject(object? v)
    {
        if (v is null) return "<null>";

        // If it's a primitive, print directly
        if (v is string s) return s;
        if (v is decimal dec) return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v is double dbl) return dbl.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
        if (v is float flt) return flt.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
        if (v is int i) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v is long l) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v is bool b) return b ? "true" : "false";

        try
        {
            // Value objects often have .Value + .Unit or similar
            var t = v.GetType();
            var value = GetMemberValue(v, t, "Value");
            var unit = GetMemberValue(v, t, "Unit") ?? GetMemberValue(v, t, "UnitKind");

            var isMm = GetMemberValue(v, t, "IsMm");
            var isDeg = GetMemberValue(v, t, "IsDeg");

            // Try AsMm/AsDeg if present
            var asMm = TryInvokeNoArgs(v, t, "AsMm");
            var asDeg = TryInvokeNoArgs(v, t, "AsDeg");

            if (asMm != null) return $"{asMm}mm";
            if (asDeg != null) return $"{asDeg}deg";

            if (value != null && unit != null) return $"{value} {unit}";
            if (value != null) return $"{value}";

            if (isMm is bool mm && mm) return v + " (IsMm)";
            if (isDeg is bool dg && dg) return v + " (IsDeg)";

            return v.ToString() ?? "<null>";
        }
        catch
        {
            return v.ToString() ?? "<null>";
        }
    }

    private static object? TryInvokeNoArgs(object obj, Type t, string methodName)
    {
        try
        {
            var m = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return null;
            if (m.GetParameters().Length != 0) return null;
            return m.Invoke(obj, null);
        }
        catch
        {
            return null;
        }
    }
}

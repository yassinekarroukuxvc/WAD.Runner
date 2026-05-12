using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using WAD.Runner.DataManagement.Domain.Dimensions;

namespace WAD.Runner.DataManagement.Domain.Wedge;

public static class WedgeDebug
{
    public static void DumpWedgeData(WedgeData wedge, string tag = "WedgeData")
    {
        if (wedge is null)
        {
            Debug.WriteLine($"[{tag}] wedge is null");
            return;
        }

        Debug.WriteLine(FormatWedgeData(wedge, tag));
    }

    public static string FormatWedgeData(WedgeData wedge, string tag = "WedgeData")
    {
        if (wedge is null)
            return $"[{tag}] wedge is null";

        var sb = new StringBuilder(16_384);

        sb.AppendLine($"[{tag}] ArticleNumber: {wedge.ArticleNumber}");
        sb.AppendLine($"[{tag}] Subclass: {wedge.Subclass}");
        sb.AppendLine($"[{tag}] KValue: {(wedge.KValue is null ? "<null>" : wedge.KValue.ToString())}");
        sb.AppendLine($"[{tag}] Marking: {(wedge.Marking is null ? "<null>" : wedge.Marking.ToString())}");

        sb.AppendLine($"[{tag}] Properties: count={wedge.Properties?.Count ?? 0}");
        if (wedge.Properties is not null)
        {
            foreach (var kv in wedge.Properties.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"[{tag}] PROP {kv.Key} = {(kv.Value is null ? "<null>" : kv.Value)}");
        }

        sb.AppendLine($"[{tag}] Dimensions: count={wedge.Dimensions?.Count ?? 0}");
        if (wedge.Dimensions is not null)
        {
            foreach (var kv in wedge.Dimensions.OrderBy(k => k.Key.ToString(), StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"[{tag}] DIM {kv.Key}: {FormatDimension(kv.Value)}");
        }

        return sb.ToString();
    }

    private static string FormatDimension(Dimension? dim)
    {
        if (dim is null)
            return "<null>";

        try
        {
            var t = dim.GetType();
            var nominal = GetMemberValue(dim, t, "Nominal");
            var lowerTolerance = GetMemberValue(dim, t, "Ltol")
                                 ?? GetMemberValue(dim, t, "LowerTolerance")
                                 ?? GetMemberValue(dim, t, "LTol");
            var upperTolerance = GetMemberValue(dim, t, "Utol")
                                 ?? GetMemberValue(dim, t, "UpperTolerance")
                                 ?? GetMemberValue(dim, t, "UTol");
            var comment = GetMemberValue(dim, t, "Comment") ?? GetMemberValue(dim, t, "Note");

            var parts = new List<string> { $"Nom={FormatValueObject(nominal)}" };

            if (lowerTolerance is not null)
                parts.Add($"Ltol={FormatValueObject(lowerTolerance)}");
            if (upperTolerance is not null)
                parts.Add($"Utol={FormatValueObject(upperTolerance)}");
            if (comment is not null)
                parts.Add($"Comment={comment}");

            var text = dim.ToString();
            if (!string.IsNullOrWhiteSpace(text) && text != t.FullName)
                parts.Add($"ToString={text}");

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
            var property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
                return property.GetValue(obj);

            var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatValueObject(object? value)
    {
        if (value is null) return "<null>";
        if (value is string text) return text;
        if (value is decimal dec) return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is double dbl) return dbl.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
        if (value is float flt) return flt.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
        if (value is int i) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is long l) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is bool b) return b ? "true" : "false";

        try
        {
            var t = value.GetType();
            var memberValue = GetMemberValue(value, t, "Value");
            var unit = GetMemberValue(value, t, "Unit") ?? GetMemberValue(value, t, "UnitKind");
            var asMillimeters = TryInvokeNoArgs(value, t, "AsMm");
            var asDegrees = TryInvokeNoArgs(value, t, "AsDeg");

            if (asMillimeters is not null) return $"{asMillimeters}mm";
            if (asDegrees is not null) return $"{asDegrees}deg";
            if (memberValue is not null && unit is not null) return $"{memberValue} {unit}";
            if (memberValue is not null) return $"{memberValue}";

            return value.ToString() ?? "<null>";
        }
        catch
        {
            return value.ToString() ?? "<null>";
        }
    }

    private static object? TryInvokeNoArgs(object obj, Type t, string methodName)
    {
        try
        {
            var method = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null || method.GetParameters().Length != 0)
                return null;

            return method.Invoke(obj, null);
        }
        catch
        {
            return null;
        }
    }
}

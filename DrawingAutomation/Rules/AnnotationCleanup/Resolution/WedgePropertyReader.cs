using System;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class WedgePropertyReader
{
    public static string? GetPropLoose(WedgeData wedge, string key)
    {
        if (wedge?.Properties == null || string.IsNullOrWhiteSpace(key)) return null;

        if (wedge.Properties.TryGetValue(key, out var value))
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        foreach (var kv in wedge.Properties)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
        }

        return null;
    }
}

using System;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Core;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

public static class WedgePropertyReader
{
    public static string? GetPropLoose(
        WedgeData wedge,
        string key)
        => wedge is null
            ? null
            : new DrawingWedgeFacts(wedge).GetProperty(key);

    public static string? GetFirstPropLoose(
        WedgeData wedge,
        params string[] keys)
    {
        if (wedge is null || keys is null)
            return null;

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = GetPropLoose(wedge, key);

            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}

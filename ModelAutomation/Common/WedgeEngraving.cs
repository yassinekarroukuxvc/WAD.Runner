using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Common;

/// <summary>Engraving text from the database (FG: Wed-Engrave, PGB: PGB-Engrave), or null.</summary>
public static class WedgeEngraving
{
    public static string? Resolve(WedgeData? wedge)
    {
        if (wedge?.Properties is null)
            return null;

        var key = wedge.Subclass == WedgeSubclass.PGB ? "PGB-Engrave" : "Wed-Engrave";
        if (!wedge.Properties.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        // Spec payloads carry trailing ';' fields; the text is the first one.
        var text = raw.Split(';')[0].Trim();
        return text.Length == 0 ? null : text;
    }
}

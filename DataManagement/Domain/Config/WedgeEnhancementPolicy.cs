using System;
using System.IO;
using System.Text.Json;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Config;

/// <summary>
/// Optional template-dependent enhancements configured per wedge.
/// Missing settings fail closed. Restart the runner after editing the JSON.
/// The config file also works for installations launched from bin/Release.
/// </summary>
public static class WedgeEnhancementPolicy
{
    private static readonly Lazy<JsonDocument?> Config = new(Load);

    public static bool SplitDimensionTables(WedgeType wedgeType) => Enabled(wedgeType, "SplitDimensionTables");
    public static bool FilletCenterMarks(WedgeType wedgeType) => Enabled(wedgeType, "FilletCenterMarks");
    public static bool Engraving(WedgeType wedgeType) => Enabled(wedgeType, "Engraving");

    public static bool Enabled(WedgeType wedgeType, string feature)
    {
        var doc = Config.Value;
        if (doc is null)
            return wedgeType == WedgeType._4516; // Backward compatible default for 4516.
        var root = doc.RootElement;
        if (root.TryGetProperty("Wedges", out var wedges) &&
            wedges.TryGetProperty(wedgeType.ToString(), out var current) &&
            ReadBoolean(current, feature) is bool wedgeValue)
            return wedgeValue;
        if (root.TryGetProperty("Defaults", out var defaults) &&
            ReadBoolean(defaults, feature) is bool defaultValue)
            return defaultValue;
        return false;
    }

    private static bool? ReadBoolean(JsonElement parent, string key)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(key, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static JsonDocument? Load()
    {
        var relative = Path.Combine("Resources", "Config", "wedge_enhancements.json");
        var possible = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), relative),
            Path.Combine(AppContext.BaseDirectory, relative)
        };
        foreach (var path in possible)
        {
            if (File.Exists(path))
                return JsonDocument.Parse(File.ReadAllText(path));
        }
        return null;
    }
}

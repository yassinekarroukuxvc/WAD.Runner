using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class DimensionFacts
{
    private readonly IReadOnlyDictionary<string, bool> _positiveByKey;

    public DimensionFacts(IReadOnlyDictionary<string, bool> positiveByKey)
    {
        _positiveByKey = positiveByKey ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsPositive(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _positiveByKey.TryGetValue(Normalize(key), out var value) && value;
    }

    public bool ArePositive(params string[] keys)
        => keys != null && keys.Length > 0 && keys.All(IsPositive);

    public IReadOnlyDictionary<string, bool> Snapshot()
        => new Dictionary<string, bool>(_positiveByKey, StringComparer.OrdinalIgnoreCase);

    public static DimensionFacts FromBooleans(IReadOnlyDictionary<string, bool> values)
    {
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in values ?? new Dictionary<string, bool>())
            dict[Normalize(kv.Key)] = kv.Value;
        return new DimensionFacts(dict);
    }

    public static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var s = key.Trim().ToUpperInvariant();
        var at = s.IndexOf('@');
        if (at >= 0) s = s[..at];
        s = s.Replace("-", "_").Replace(" ", "_").Replace("(", "_").Replace(")", string.Empty);
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

public sealed class DimensionFacts
{
    private readonly IReadOnlyDictionary<string, bool> _positiveByKey;
    private readonly HashSet<string> _presentKeys;

    /// <summary>
    /// Backward-compatible constructor. Every supplied key is considered
    /// present, while its boolean value indicates whether it is positive.
    /// </summary>
    public DimensionFacts(
        IReadOnlyDictionary<string, bool> positiveByKey)
        : this(
            positiveByKey,
            positiveByKey?.Keys)
    {
    }

    public DimensionFacts(
        IReadOnlyDictionary<string, bool>? positiveByKey,
        IEnumerable<string>? presentKeys)
    {
        var positives = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var kv in positiveByKey ??
                 new Dictionary<string, bool>())
        {
            var key = Normalize(kv.Key);

            if (!string.IsNullOrWhiteSpace(key))
                positives[key] = kv.Value;
        }

        _positiveByKey = positives;
        _presentKeys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rawKey in presentKeys ?? Array.Empty<string>())
        {
            var key = Normalize(rawKey);

            if (!string.IsNullOrWhiteSpace(key))
                _presentKeys.Add(key);
        }

    }

    /// <summary>
    /// Returns true when the dimension existed in the source wedge data,
    /// regardless of whether its nominal value is zero or positive.
    /// </summary>
    public bool IsPresent(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return _presentKeys.Contains(Normalize(key));
    }

    public bool IsPositive(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return _positiveByKey.TryGetValue(
                   Normalize(key),
                   out var value) &&
               value;
    }

    public bool ArePresent(params string[] keys)
        => keys is { Length: > 0 } && keys.All(IsPresent);

    public bool ArePositive(params string[] keys)
        => keys is { Length: > 0 } && keys.All(IsPositive);

    public static DimensionFacts FromPresenceAndBooleans(
        IReadOnlyDictionary<string, bool> values,
        IEnumerable<string> presentKeys)
        => new(values, presentKeys);

    public static string Normalize(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var s = key.Trim().ToUpperInvariant();
        var at = s.IndexOf('@');

        if (at >= 0)
            s = s[..at];

        s = s
            .Replace("-", "_")
            .Replace(" ", "_")
            .Replace("(", "_")
            .Replace(")", string.Empty);

        return new string(
            s.Where(char.IsLetterOrDigit)
                .ToArray());
    }
}

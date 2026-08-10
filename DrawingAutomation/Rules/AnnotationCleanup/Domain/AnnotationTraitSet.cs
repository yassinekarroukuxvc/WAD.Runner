using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

/// <summary>
/// Normalized wedge-specific values used by annotation conditions.
///
/// The shared cleanup engine knows only trait names and normalized values.
/// Each wedge module owns the mapping from its database properties to these
/// traits, so adding another wedge type does not require adding values to a
/// central enum or adding wedge-specific branches to the context factory.
/// </summary>
public sealed class AnnotationTraitSet
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public static AnnotationTraitSet Empty { get; } =
        new(Array.Empty<KeyValuePair<string, string>>());

    public AnnotationTraitSet(
        IEnumerable<KeyValuePair<string, string>> values)
    {
        var resolved = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values ??
                 Array.Empty<KeyValuePair<string, string>>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            resolved[pair.Key.Trim()] =
                AnnotationTokenNormalizer.Normalize(pair.Value);
        }

        _values = resolved;
    }

    public string Get(string traitName)
    {
        if (string.IsNullOrWhiteSpace(traitName))
            return string.Empty;

        return _values.TryGetValue(
            traitName.Trim(),
            out var value)
                ? value
                : string.Empty;
    }

    public bool IsAny(
        string traitName,
        IEnumerable<string> acceptedValues)
    {
        var actual = Get(traitName);

        if (string.IsNullOrWhiteSpace(actual))
            return false;

        return (acceptedValues ?? Array.Empty<string>())
            .Select(AnnotationTokenNormalizer.Normalize)
            .Any(expected => string.Equals(
                actual,
                expected,
                StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string> AsReadOnlyDictionary()
        => _values;
}

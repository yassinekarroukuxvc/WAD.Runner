using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;

/// <summary>
/// Centralizes annotation-name comparison.
///
/// SolidWorks normally reports a displayed model dimension as:
///     Dimension@Owner@ReferencedDocument
/// while rule catalogs intentionally store only:
///     Dimension@Owner
///
/// The document suffix is therefore ignored, but the dimension and owner names
/// are compared exactly. Prefix and substring matching are deliberately forbidden.
/// </summary>
public static class AnnotationNameIdentity
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var compact = new string(value.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        var parts = compact.Split('@');

        if (parts.Length >= 2)
            compact = parts[0] + "@" + parts[1];

        return compact.ToUpperInvariant();
    }

    public static string GetDimensionName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var compact = new string(value.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
        var at = compact.IndexOf('@');
        return (at < 0 ? compact : compact[..at]).ToUpperInvariant();
    }

    public static bool AreEquivalent(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Produces only known, safe template spelling variants.
    /// No starts-with, contains, or fuzzy matching is used.
    /// </summary>
    public static IReadOnlyCollection<string> GetSafeCandidateIdentities(string? expectedFullName)
    {
        if (string.IsNullOrWhiteSpace(expectedFullName))
            return Array.Empty<string>();

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? candidate)
        {
            var normalized = Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
                candidates.Add(normalized);
        }

        Add(expectedFullName);

        if (expectedFullName.EndsWith("_sketch", StringComparison.OrdinalIgnoreCase))
            Add(expectedFullName[..^"_sketch".Length]);

        if (expectedFullName.Contains("_FRONT_FRONT_", StringComparison.OrdinalIgnoreCase))
        {
            Add(expectedFullName.Replace(
                "_FRONT_FRONT_",
                "_FRONT_",
                StringComparison.OrdinalIgnoreCase));
        }

        var at = expectedFullName.IndexOf('@');
        if (at > 0 && at < expectedFullName.Length - 1)
        {
            var dimension = expectedFullName[..at];
            var owner = expectedFullName[(at + 1)..];

            if (owner.EndsWith("_sketch", StringComparison.OrdinalIgnoreCase))
                Add(dimension + "@" + owner[..^"_sketch".Length]);

            if (owner.Contains("_FRONT_FRONT_", StringComparison.OrdinalIgnoreCase))
            {
                Add(dimension + "@" + owner.Replace(
                    "_FRONT_FRONT_",
                    "_FRONT_",
                    StringComparison.OrdinalIgnoreCase));
            }
        }

        return candidates.ToList().AsReadOnly();
    }
}

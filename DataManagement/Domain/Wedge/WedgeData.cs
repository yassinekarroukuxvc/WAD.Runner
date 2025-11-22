using WAD.Runner.DataManagement.Domain.Dimensions;

namespace WAD.Runner.DataManagement.Domain.Wedge;

/// <summary>
/// Aggregate root for wedge input data (FG/PGB), unit-normalized:
/// - Lengths in mm, angles in deg.
/// - Tolerances (mm) on length-only dimensions.
/// - Properties holds template-specific fields (e.g., Wed-Polish, PGB-Remarks).
/// </summary>
public sealed record WedgeData
{
    public string ArticleNumber { get; }
    public WedgeSubclass Subclass { get; }
    public IReadOnlyDictionary<DimensionKey, Dimension> Dimensions { get; }
    public KValue? KValue { get; }
    public WedMarking? Marking { get; }
    public IReadOnlyDictionary<string, string?> Properties { get; }

    public WedgeData(
        string articleNumber,
        WedgeSubclass subclass,
        IReadOnlyDictionary<DimensionKey, Dimension> dimensions,
        KValue? kValue,
        WedMarking? marking,
        IReadOnlyDictionary<string, string?> properties)
    {
        if (string.IsNullOrWhiteSpace(articleNumber))
            throw new ArgumentException("ArticleNumber cannot be empty.", nameof(articleNumber));
        ArticleNumber = articleNumber.Trim();

        Subclass = subclass;
        Dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        KValue = kValue;
        Marking = marking;

        // Normalize property keys to a consistent casing (keep values as-is).
        Properties = properties is null
            ? throw new ArgumentNullException(nameof(properties))
            : new Dictionary<string, string?>(properties, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Convenience getter; returns null if key is absent.</summary>
    public Dimension? TryGet(DimensionKey key)
        => Dimensions.TryGetValue(key, out var d) ? d : null;

    /// <summary>Whether a dimension exists.</summary>
    public bool Has(DimensionKey key) => Dimensions.ContainsKey(key);
}

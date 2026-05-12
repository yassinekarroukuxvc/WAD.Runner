using WAD.Runner.DataManagement.Domain.Dimensions;

namespace WAD.Runner.DataManagement.Domain.Wedge;

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

        Properties = properties is null
            ? throw new ArgumentNullException(nameof(properties))
            : new Dictionary<string, string?>(properties, StringComparer.OrdinalIgnoreCase);
    }

    public Dimension? TryGet(DimensionKey key)
        => Dimensions.TryGetValue(key, out var d) ? d : null;

    public bool Has(DimensionKey key) => Dimensions.ContainsKey(key);
}

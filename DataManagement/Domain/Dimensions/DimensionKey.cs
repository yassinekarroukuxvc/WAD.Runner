namespace WAD.Runner.DataManagement.Domain.Dimensions;

/// <summary>
/// Strongly-typed key for a geometric dimension (e.g., "TL", "FL", "BA").
/// Keeps domain code from passing raw strings around.
/// </summary>
public readonly record struct DimensionKey(string Value)
{
    public static DimensionKey From(string s) => new(s?.Trim() ?? string.Empty);

    public override string ToString() => Value;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}

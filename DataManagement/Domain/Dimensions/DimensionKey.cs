namespace WAD.Runner.DataManagement.Domain.Dimensions;

public readonly record struct DimensionKey(string Value)
{
    public static DimensionKey From(string s) => new(s?.Trim() ?? string.Empty);

    public override string ToString() => Value;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}

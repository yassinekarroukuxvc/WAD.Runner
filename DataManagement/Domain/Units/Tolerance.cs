namespace WAD.Runner.DataManagement.Domain.Units;

/// <summary>
/// Lower/Upper tolerances (always in millimeters).
/// Use the Mm(...) factory to construct safely.
/// </summary>
public sealed record Tolerance
{
    public Quantity Lower { get; }
    public Quantity Upper { get; }

    private Tolerance(Quantity lower, Quantity upper)
    {
        if (lower.Unit != UnitKind.Millimeter || upper.Unit != UnitKind.Millimeter)
            throw new ArgumentException("Tolerance must be expressed in millimeters.");
        Lower = lower;
        Upper = upper;
    }

    /// <summary>Factory from decimals (mm).</summary>
    public static Tolerance Mm(decimal lower, decimal upper)
        => new(Quantity.MmOf(lower), Quantity.MmOf(upper));

    public static readonly Tolerance Zero = new(Quantity.MmOf(0m), Quantity.MmOf(0m));

    public bool IsZero => Lower.Value == 0m && Upper.Value == 0m;
}

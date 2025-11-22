using WAD.Runner.DataManagement.Domain.Units;

namespace WAD.Runner.DataManagement.Domain.Wedge;

/// <summary>
/// Wedge K-Value captured in millimeters with optional comment.
/// Parsed from payload format: "k_mm;comment;;;;;;".
/// </summary>
public sealed class KValue
{
    public Quantity ValueMm { get; }
    public string? Comment { get; }

    public KValue(Quantity valueMm, string? comment)
    {
        if (valueMm.Unit != UnitKind.Millimeter)
            throw new ArgumentException("K-Value must be expressed in millimeters.", nameof(valueMm));

        ValueMm = valueMm;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public override string ToString() =>
        Comment is null
            ? $"{ValueMm}"
            : $"{ValueMm} ({Comment})";
}

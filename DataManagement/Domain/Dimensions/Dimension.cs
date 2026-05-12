using WAD.Runner.DataManagement.Domain.Units;

namespace WAD.Runner.DataManagement.Domain.Dimensions;

public sealed record Dimension
{
    public DimensionKey Key { get; }
    public Quantity Nominal { get; }
    public Tolerance Tol { get; }
    public string? Comment { get; }

    private Dimension(DimensionKey key, Quantity nominal, Tolerance tol, string? comment)
    {
        if (key.IsEmpty) throw new ArgumentException("DimensionKey cannot be empty.", nameof(key));

        if (nominal.Unit == UnitKind.Degree && !tol.IsZero)
            throw new ArgumentException("Angular dimensions should not carry length tolerances.", nameof(tol));

        Key = key;
        Nominal = nominal;
        Tol = tol;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    public static Dimension CreateLength(DimensionKey key, Quantity nominalMm, Tolerance tolMm, string? comment = null)
    {
        if (nominalMm.Unit != UnitKind.Millimeter)
            throw new ArgumentException("Length nominal must be in millimeters.", nameof(nominalMm));
        return new Dimension(key, nominalMm, tolMm, comment);
    }

    public static Dimension CreateAngle(DimensionKey key, Quantity nominalDeg, string? comment = null)
    {
        if (nominalDeg.Unit != UnitKind.Degree)
            throw new ArgumentException("Angle nominal must be in degrees.", nameof(nominalDeg));
        return new Dimension(key, nominalDeg, Tolerance.Zero, comment);
    }
}

using System.Globalization;

namespace WAD.Runner.DataManagement.Domain.Units;

public readonly record struct Quantity(decimal Value, UnitKind Unit)
{
    public static Quantity MmOf(decimal v) => new(v, UnitKind.Millimeter);
    public static Quantity DegOf(decimal v) => new(v, UnitKind.Degree);

    public bool IsMm => Unit == UnitKind.Millimeter;
    public bool IsDeg => Unit == UnitKind.Degree;

    public decimal AsMm() => Unit == UnitKind.Millimeter ? Value : throw new InvalidOperationException("Quantity is not in millimeters.");
    public decimal AsDeg() => Unit == UnitKind.Degree ? Value : throw new InvalidOperationException("Quantity is not in degrees.");

    public static Quantity operator +(Quantity a, Quantity b)
        => a.Unit == b.Unit
            ? new Quantity(a.Value + b.Value, a.Unit)
            : throw new InvalidOperationException($"Cannot add {a.Unit} to {b.Unit}.");

    public static Quantity operator -(Quantity a, Quantity b)
        => a.Unit == b.Unit
            ? new Quantity(a.Value - b.Value, a.Unit)
            : throw new InvalidOperationException($"Cannot subtract {b.Unit} from {a.Unit}.");

    public static Quantity operator *(Quantity a, decimal k) => new(a.Value * k, a.Unit);
    public static Quantity operator *(decimal k, Quantity a) => new(a.Value * k, a.Unit);
    public static Quantity operator /(Quantity a, decimal k) => new(a.Value / k, a.Unit);

    public override string ToString()
        => Unit == UnitKind.Millimeter
            ? $"{Value.ToString(CultureInfo.InvariantCulture)} mm"
            : $"{Value.ToString(CultureInfo.InvariantCulture)} deg";
}

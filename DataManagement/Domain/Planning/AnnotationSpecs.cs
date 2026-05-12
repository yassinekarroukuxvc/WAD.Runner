using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;

namespace WAD.Runner.DataManagement.Domain.Planning;

public interface IAnnotationSpec
{
    string Id { get; }
    double[] PositionMm { get; }
    int Z { get; }
}

public enum DimAxis { Horizontal, Vertical }

[Flags]
public enum DimStyle
{
    None = 0,
    Reference = 1 << 0,
    Min = 1 << 1,
    Hidden = 1 << 2
}

public sealed class DimensionSpec : IAnnotationSpec
{
    public required string Id { get; init; }
    public required string View { get; init; }
    public required DimensionKey Key { get; init; }
    public required double[] PositionMm { get; init; }
    public required DimAxis Axis { get; init; }

    public required Quantity Nominal { get; init; }
    public required Tolerance Tol { get; init; }
    public string? Comment { get; init; }

    public DimStyle Style { get; init; } = DimStyle.None;
    public int Z { get; init; } = 10;
    public string? TextOverride { get; init; }
}

public sealed class NoteSpec : IAnnotationSpec
{
    public required string Id { get; init; }
    public required double[] PositionMm { get; init; }
    public required string Text { get; init; }
    public int Z { get; init; } = 1;
}

public sealed class TableSpec : IAnnotationSpec
{
    public required string Id { get; init; }
    public required double[] PositionMm { get; init; }
    public double[]? SizeMm { get; init; }
    public int Z { get; init; } = 0;
}

using System;

using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Planning.Rules;

/// <summary>
/// 4516 Production/Customer display length: the single source for the model TL used by the
/// equation planner, the dimension rules and the breaklines (the table always shows the real TL).
///
/// Up to the reference TD the model is 12 mm long and drawn at the configured scale (7:1, 84 mm).
/// A thicker tool is drawn at a proportionally smaller scale, so its Front/Side/Top keep about the
/// same width on paper and leave room for Detail/Section; the model length grows by the same
/// factor, so the tool is still drawn the same length.
/// </summary>
public static class _4516DisplayLength
{
    public const double BaseTlMm = 12.0;
    public const double ReferenceTdMm = 1.585;

    /// <summary>1 up to the reference TD, TD / reference above it.</summary>
    public static double Factor(double tdMm)
        => double.IsFinite(tdMm) && tdMm > ReferenceTdMm ? tdMm / ReferenceTdMm : 1.0;

    public static double TlMm(double tdMm) => BaseTlMm * Factor(tdMm);

    public static double TlMm(WedgeData wedge)
    {
        var td = wedge.TryGet(DimensionKey.From("TD"));
        return TlMm(td is not null && td.Nominal.Unit == UnitKind.Millimeter ? (double)td.Nominal.Value : 0.0);
    }
}
